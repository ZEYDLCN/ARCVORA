using System.Net;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Ai;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arcvora.Integration.Tests;

/// <summary>Canlı API anahtarı olmadan istemcinin HTTP sözleşmesini doğrular. Canlı kalite bu testlerin kapsamı değildir.</summary>
public class AnthropicClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond(request);
        }
    }

    private static readonly StructuredRequest Req = new("sistem", "kullanıcı", "submit_model_draft", "açıklama",
        JsonDocument.Parse("""{"type":"object","properties":{"summary":{"type":"string"}}}""").RootElement.Clone(), 1234);

    private static AnthropicLlmClient Client(StubHandler h, string key = "sk-ant-secret-key") => new(
        new HttpClient(h) { BaseAddress = null },
        Options.Create(new AiOptions { Anthropic = new AnthropicOptions { ApiKey = key, Model = "claude-sonnet-5-5", BaseUrl = "https://api.example.test" } }));

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Sends_a_forced_tool_call_with_required_headers_and_parses_usage()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"model":"claude-sonnet-5-5","stop_reason":"tool_use","usage":{"input_tokens":321,"output_tokens":45},
             "content":[{"type":"text","text":"düşünüyorum"},{"type":"tool_use","id":"t1","name":"submit_model_draft","input":{"summary":"tamam"}}]}
            """));
        var r = await Client(h).GenerateAsync(Req, CancellationToken.None);

        Assert.Equal("""{"summary":"tamam"}""", r.Json);
        Assert.Equal(321, r.InputTokens);
        Assert.Equal(45, r.OutputTokens);

        Assert.Equal("https://api.example.test/v1/messages", h.Last!.RequestUri!.ToString());
        Assert.Equal("sk-ant-secret-key", h.Last.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", h.Last.Headers.GetValues("anthropic-version").Single());

        using var body = JsonDocument.Parse(h.LastBody!);
        var root = body.RootElement;
        Assert.Equal("claude-sonnet-5-5", root.GetProperty("model").GetString());
        Assert.Equal(1234, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("sistem", root.GetProperty("system").GetString());
        Assert.Equal("kullanıcı", root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("tool", root.GetProperty("tool_choice").GetProperty("type").GetString());
        Assert.Equal("submit_model_draft", root.GetProperty("tool_choice").GetProperty("name").GetString());
        Assert.Equal("submit_model_draft", root.GetProperty("tools")[0].GetProperty("name").GetString());
        Assert.Equal("object", root.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData((HttpStatusCode)529)]
    public async Task Rate_limit_and_server_errors_are_transient_and_retryable(HttpStatusCode code)
    {
        var h = new StubHandler(_ => Json(code, """{"type":"error","error":{"type":"overloaded_error","message":"bekleyin"}}"""));
        await Assert.ThrowsAsync<LlmTransientException>(() => Client(h).GenerateAsync(Req, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Client_errors_are_permanent_and_never_echo_the_api_key(HttpStatusCode code)
    {
        var h = new StubHandler(_ => Json(code, """{"type":"error","error":{"type":"invalid_request_error","message":"model bulunamadı"}}"""));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Client(h).GenerateAsync(Req, CancellationToken.None));
        Assert.DoesNotContain("sk-ant-secret-key", ex.Message);
        Assert.Contains(((int)code).ToString(), ex.Message);
    }

    [Fact]
    public async Task Truncated_output_is_a_permanent_failure_not_a_silent_partial_result()
    {
        var h = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"stop_reason":"max_tokens","usage":{"input_tokens":1,"output_tokens":1},"content":[{"type":"tool_use","name":"submit_model_draft","input":{"summary":"yarım"}}]}"""));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Client(h).GenerateAsync(Req, CancellationToken.None));
        Assert.Contains("kesildi", ex.Message);
    }

    [Fact]
    public async Task Missing_tool_call_and_malformed_bodies_are_handled()
    {
        var noTool = new StubHandler(_ => Json(HttpStatusCode.OK, """{"stop_reason":"end_turn","content":[{"type":"text","text":"merhaba"}]}"""));
        await Assert.ThrowsAsync<LlmException>(() => Client(noTool).GenerateAsync(Req, CancellationToken.None));

        var garbage = new StubHandler(_ => Json(HttpStatusCode.OK, "<html>not json</html>"));
        await Assert.ThrowsAsync<LlmTransientException>(() => Client(garbage).GenerateAsync(Req, CancellationToken.None));
    }

    [Fact]
    public async Task Network_failure_is_transient()
    {
        var h = new StubHandler(_ => throw new HttpRequestException("bağlantı reddedildi"));
        await Assert.ThrowsAsync<LlmTransientException>(() => Client(h).GenerateAsync(Req, CancellationToken.None));
    }

    [Fact]
    public async Task Caller_cancellation_is_not_misreported_as_a_provider_timeout()
    {
        using var cts = new CancellationTokenSource();
        var h = new StubHandler(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(h).GenerateAsync(Req, cts.Token));
    }

    [Fact]
    public void Startup_fails_loudly_when_the_anthropic_provider_has_no_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Arcvora"] = "Host=x", ["Ai:Provider"] = "anthropic",
        }).Build();
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddArcvoraInfrastructure(config));
        Assert.Contains("ApiKey", ex.Message);

        var unknown = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Arcvora"] = "Host=x", ["Ai:Provider"] = "gpt-yok",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddArcvoraInfrastructure(unknown));
    }
}
