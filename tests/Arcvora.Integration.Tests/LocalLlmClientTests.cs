using System.Net;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Ai;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arcvora.Integration.Tests;

/// <summary>
/// Yerel (OpenAI uyumlu) sunucu istemcisinin HTTP sözleşmesi ve çıktı şeması. Gerçek bir Qwen çalıştırması DEĞİLDİR;
/// model kalitesi bu testlerin kapsamı dışındadır.
/// </summary>
public class LocalLlmClientTests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Last = r;
            Body = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return respond(r);
        }
    }

    private static readonly StructuredRequest Req = new("sistem", "kullanıcı", "submit_model_draft", "açıklama",
        JsonDocument.Parse("""{"type":"object","properties":{"summary":{"type":"string"}}}""").RootElement.Clone(), 20000);

    private static OpenAiCompatibleLlmClient Client(Stub s, Action<LocalLlmOptions>? tweak = null)
    {
        var local = new LocalLlmOptions { BaseUrl = "http://localhost:11434", Model = "qwen2.5:7b-instruct" };
        tweak?.Invoke(local);
        return new OpenAiCompatibleLlmClient(new HttpClient(s), Options.Create(new AiOptions { Local = local }));
    }

    private static HttpResponseMessage Json(HttpStatusCode c, string body) => new(c) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Completion(string content, string finish = "stop", bool usage = true)
    {
        var o = new Dictionary<string, object?>
        {
            ["model"] = "qwen2.5:7b-instruct",
            ["choices"] = new[] { new { index = 0, finish_reason = finish, message = new { role = "assistant", content } } },
        };
        if (usage) o["usage"] = new { prompt_tokens = 812, completion_tokens = 96 };
        return JsonSerializer.Serialize(o);
    }

    [Fact]
    public async Task Sends_a_schema_constrained_chat_completion_and_parses_usage()
    {
        var stub = new Stub(_ => Json(HttpStatusCode.OK, Completion("""{"summary":"tamam"}""")));
        var r = await Client(stub).GenerateAsync(Req, CancellationToken.None);

        Assert.Equal("""{"summary":"tamam"}""", r.Json);
        Assert.Equal((812, 96), (r.InputTokens, r.OutputTokens));
        Assert.Equal("http://localhost:11434/v1/chat/completions", stub.Last!.RequestUri!.ToString());
        Assert.NotNull(stub.Last.Content!.Headers.ContentLength);                // chunked değil: Content-Length ile gider
        Assert.Null(stub.Last.Headers.Authorization);                           // anahtarsız yerel sunucu

        using var body = JsonDocument.Parse(stub.Body!);
        var root = body.RootElement;
        Assert.Equal("qwen2.5:7b-instruct", root.GetProperty("model").GetString());
        Assert.Equal(8192, root.GetProperty("max_tokens").GetInt32());          // istek 20000 istese de yerel üst sınır geçerli
        Assert.Equal(0.2, root.GetProperty("temperature").GetDouble());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal("kullanıcı", root.GetProperty("messages")[1].GetProperty("content").GetString());
        var rf = root.GetProperty("response_format");
        Assert.Equal("json_schema", rf.GetProperty("type").GetString());
        Assert.True(rf.GetProperty("json_schema").GetProperty("strict").GetBoolean());
        Assert.Equal("object", rf.GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Optional_api_key_and_no_think_switch_are_honoured()
    {
        var stub = new Stub(_ => Json(HttpStatusCode.OK, Completion("{}")));
        await Client(stub, o => { o.ApiKey = "yerel-anahtar"; o.DisableThinking = true; }).GenerateAsync(Req, CancellationToken.None);
        Assert.Equal("Bearer yerel-anahtar", stub.Last!.Headers.Authorization!.ToString());
        using var body = JsonDocument.Parse(stub.Body!);
        Assert.EndsWith("/no_think", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("<think>önce düşüneyim...</think>\n{\"summary\":\"x\"}", "{\"summary\":\"x\"}")]
    [InlineData("```json\n{\"summary\":\"x\"}\n```", "{\"summary\":\"x\"}")]
    [InlineData("İşte taslak: {\"summary\":\"x\"} umarım yardımcı olur.", "{\"summary\":\"x\"}")]
    [InlineData("  {\"summary\":\"x\"}  ", "{\"summary\":\"x\"}")]
    public void Cleans_thinking_blocks_fences_and_chatter_around_the_json(string raw, string expected) =>
        Assert.Equal(expected, OpenAiCompatibleLlmClient.CleanJson(raw));

    [Fact]
    public async Task Estimates_tokens_when_the_server_reports_no_usage_so_the_budget_still_applies()
    {
        var stub = new Stub(_ => Json(HttpStatusCode.OK, Completion("""{"summary":"tamam"}""", usage: false)));
        var r = await Client(stub).GenerateAsync(Req, CancellationToken.None);
        Assert.True(r.InputTokens > 0);
        Assert.True(r.OutputTokens > 0);
    }

    [Fact]
    public async Task Truncated_output_is_permanent_and_explains_the_context_gotcha()
    {
        var stub = new Stub(_ => Json(HttpStatusCode.OK, Completion("""{"summary":"yar""", finish: "length")));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Client(stub).GenerateAsync(Req, CancellationToken.None));
        Assert.Contains("num_ctx", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Busy_or_loading_server_is_transient(HttpStatusCode code)
    {
        var stub = new Stub(_ => Json(code, """{"error":{"message":"model loading"}}"""));
        await Assert.ThrowsAsync<LlmTransientException>(() => Client(stub).GenerateAsync(Req, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"context length exceeded"}}""")]
    [InlineData(HttpStatusCode.NotFound, """{"error":"model 'qwen' not found"}""")]
    public async Task Client_errors_are_permanent_and_carry_the_server_message(HttpStatusCode code, string body)
    {
        var stub = new Stub(_ => Json(code, body));
        var ex = await Assert.ThrowsAsync<LlmException>(() => Client(stub).GenerateAsync(Req, CancellationToken.None));
        Assert.Contains(((int)code).ToString(), ex.Message);
        Assert.True(ex.Message.Contains("context length") || ex.Message.Contains("not found"));
    }

    [Fact]
    public async Task Server_not_running_is_transient_with_a_helpful_message()
    {
        var stub = new Stub(_ => throw new HttpRequestException("Connection refused"));
        var ex = await Assert.ThrowsAsync<LlmTransientException>(() => Client(stub).GenerateAsync(Req, CancellationToken.None));
        Assert.Contains("localhost:11434", ex.Message);
    }

    [Fact]
    public async Task Empty_output_is_rejected()
    {
        var stub = new Stub(_ => Json(HttpStatusCode.OK, Completion("<think>sadece düşünce</think>")));
        await Assert.ThrowsAsync<LlmException>(() => Client(stub).GenerateAsync(Req, CancellationToken.None));
    }

    [Fact]
    public void Provider_name_shows_the_local_model_in_proposals()
    {
        var c = Client(new Stub(_ => Json(HttpStatusCode.OK, "{}")));
        Assert.Equal("local:qwen2.5:7b-instruct", c.ProviderName);
    }

    [Fact]
    public void Startup_requires_a_model_name_for_the_local_provider()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Arcvora"] = "Host=x", ["Ai:Provider"] = "local",
        }).Build();
        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddArcvoraInfrastructure(cfg));
        Assert.Contains("Ai:Local:Model", ex.Message);
    }

    // ------------------------------------------------------------------ şema

    [Fact]
    public void Draft_schema_is_strict_and_derived_from_the_model_records()
    {
        var schema = DraftSchema.Get();
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());          // bilinmeyen alan yok

        var props = schema.GetProperty("properties");
        foreach (var name in new[] { "requirements", "decisions", "components", "relationships", "entities", "flows", "traceLinks", "openQuestions", "removals", "groups", "possibleImpacts" })
            Assert.Equal("array", props.GetProperty(name).GetProperty("type").GetString());

        var req = props.GetProperty("requirements").GetProperty("items");
        var required = req.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("id", required);
        Assert.Contains("text", required);

        // enum değerleri JSON sözleşmesindeki (snake_case) değerlerdir
        var json = schema.GetRawText();
        foreach (var v in new[] { "\"functional\"", "\"existing_decision\"", "\"user_statement\"", "\"system_proposal\"", "\"depends_on\"", "\"blocked_by\"", "\"external_service\"", "\"zero_or_one_to_many\"", "\"open_question\"" })
            Assert.Contains(v, json);
        Assert.DoesNotContain("\"Functional\"", json);
    }

    [Fact]
    public void Draft_schema_is_small_and_avoids_features_grammar_converters_struggle_with()
    {
        var json = DraftSchema.Get().GetRawText();
        Assert.True(json.Length < 80_000, $"Şema çok büyük: {json.Length}");
        Assert.DoesNotContain("\"$ref\"", json);
        Assert.DoesNotContain("\"$defs\"", json);
    }

    [Fact]
    public void A_draft_shaped_like_the_schema_parses_into_the_model()
    {
        const string draft = """
        {"summary":"örnek","groups":[{"id":"g1","title":"T","description":"","objectIds":["new-1"]}],
         "requirements":[{"id":"new-1","text":"Gereksinim","kind":"functional","priority":"high","origin":"system_proposal","reviewStatus":"proposed","sourceRefs":[],"acceptanceCriteria":[]}],
         "removals":[{"type":"open_question","id":"OQ-001"}],"possibleImpacts":[{"objectId":"REQ-001","reason":"neden"}]}
        """;
        var d = ModelDraft.Parse(draft);
        Assert.Equal("new-1", d.Requirements.Single().Id);
        Assert.Equal(ObjectType.OpenQuestion, d.Removals.Single().Type);
    }
}
