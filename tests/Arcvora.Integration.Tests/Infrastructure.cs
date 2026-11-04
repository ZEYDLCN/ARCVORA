using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Arcvora.Integration.Tests;

/// <summary>Testte değiştirilebilen üretici: sağlayıcı hatalarını, yavaş çağrıları ve sahte LLM'i simüle eder.</summary>
public sealed class SwitchableGenerator : IArchitectureGenerator
{
    private static readonly IArchitectureGenerator Default = new HeuristicGenerator();
    private volatile IArchitectureGenerator _current = Default;

    public IArchitectureGenerator Current { get => _current; set => _current = value; }
    public void Reset() => _current = Default;

    public string Name => _current.Name;
    public bool IsAi => _current.IsAi;
    public Task<GeneratorOutput> ExtractRequirementsAsync(GenerationContext c, CancellationToken ct) => _current.ExtractRequirementsAsync(c, ct);
    public Task<GeneratorOutput> ProposeArchitectureAsync(GenerationContext c, CancellationToken ct) => _current.ProposeArchitectureAsync(c, ct);
    public Task<GeneratorOutput> AnalyzeChangeAsync(GenerationContext c, CancellationToken ct) => _current.AnalyzeChangeAsync(c, ct);
    public Task<GeneratorOutput?> RepairAsync(GenerationTask t, GenerationContext c, GeneratorOutput p, IReadOnlyList<string> problems, CancellationToken ct) =>
        _current.RepairAsync(t, c, p, problems, ct);
}

public sealed class ScriptedLlm(params object[] responses) : ILlmClient
{
    private readonly Queue<object> _q = new(responses);
    public string ProviderName => "fake-llm";
    public Task<StructuredResponse> GenerateAsync(StructuredRequest r, CancellationToken ct)
    {
        if (!_q.TryDequeue(out var next)) throw new InvalidOperationException("Yanıt kalmadı.");
        if (next is Exception ex) throw ex;
        return Task.FromResult(new StructuredResponse(next as string ?? JsonSerializer.Serialize(next), 2000, 1000, "fake"));
    }
}

public sealed class GateGenerator(TaskCompletionSource started, TaskCompletionSource release) : IArchitectureGenerator
{
    private readonly IArchitectureGenerator _inner = new HeuristicGenerator();
    public string Name => "gate";
    public bool IsAi => false;
    public async Task<GeneratorOutput> ExtractRequirementsAsync(GenerationContext c, CancellationToken ct)
    {
        started.TrySetResult();
        await release.Task.WaitAsync(ct);
        return await _inner.ExtractRequirementsAsync(c, ct);
    }
    public Task<GeneratorOutput> ProposeArchitectureAsync(GenerationContext c, CancellationToken ct) => _inner.ProposeArchitectureAsync(c, ct);
    public Task<GeneratorOutput> AnalyzeChangeAsync(GenerationContext c, CancellationToken ct) => _inner.AnalyzeChangeAsync(c, ct);
    public Task<GeneratorOutput?> RepairAsync(GenerationTask t, GenerationContext c, GeneratorOutput p, IReadOnlyList<string> problems, CancellationToken ct) =>
        Task.FromResult<GeneratorOutput?>(null);
}

public sealed class ThrowingGenerator(Func<Exception> make) : IArchitectureGenerator
{
    public string Name => "throwing";
    public bool IsAi => true;
    public Task<GeneratorOutput> ExtractRequirementsAsync(GenerationContext c, CancellationToken ct) => throw make();
    public Task<GeneratorOutput> ProposeArchitectureAsync(GenerationContext c, CancellationToken ct) => throw make();
    public Task<GeneratorOutput> AnalyzeChangeAsync(GenerationContext c, CancellationToken ct) => throw make();
    public Task<GeneratorOutput?> RepairAsync(GenerationTask t, GenerationContext c, GeneratorOutput p, IReadOnlyList<string> problems, CancellationToken ct) =>
        Task.FromResult<GeneratorOutput?>(null);
}

/// <summary>Her test sınıfı kendi geçici PostgreSQL veritabanını alır.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static string Admin => Environment.GetEnvironmentVariable("ARCVORA_TEST_PG")
                                   ?? "Host=localhost;Username=postgres;Password=postgres";
    private readonly string _db = "arcvora_test_" + Guid.NewGuid().ToString("N");
    public SwitchableGenerator Generator { get; } = new();
    public string ConnectionString => $"{Admin};Database={_db}";

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(Admin + ";Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{_db}\"", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var conn = new NpgsqlConnection(Admin + ";Database=postgres");
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_db}\" WITH (FORCE)", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting: Program.cs kayıt anında okuduğu değerlere (Jwt:Key, bağlantı dizesi, Worker:InProcess) ancak böyle ulaşır.
        var settings = new Dictionary<string, string>
        {
            ["ConnectionStrings:Arcvora"] = ConnectionString,
            ["Jwt:Key"] = "integration-test-key-0123456789-abcdefghijklmnopqrstuvwxyz",
            ["Worker:InProcess"] = "false",             // işleri testler deterministik biçimde kendisi çalıştırır
            ["Database:MigrateOnStartup"] = "true",
            ["RateLimit:AuthPerMinute"] = "100000",
            ["Jobs:LeaseSeconds"] = "3",
            ["Jobs:RetryBaseSeconds"] = "1",
            ["Ai:Provider"] = "heuristic",
        };
        foreach (var (k, v) in settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IArchitectureGenerator>();
            s.AddSingleton<IArchitectureGenerator>(Generator);
        });
    }

    /// <summary>Kuyruktaki bütün işleri bitene kadar çalıştırır.</summary>
    public async Task<int> RunJobsAsync(int max = 20)
    {
        var runner = Services.GetRequiredService<JobRunner>();
        var n = 0;
        while (n < max && await runner.RunOnceAsync("test-worker", CancellationToken.None)) n++;
        return n;
    }
}

public sealed record Resp(HttpStatusCode Status, JsonElement Body)
{
    public JsonElement this[string name] => Body.GetProperty(name);
    public string Str(string name) => Body.GetProperty(name).GetString()!;
    public bool Is(HttpStatusCode s) => Status == s;
    public override string ToString() => $"{(int)Status} {Body}";
}

public sealed class Api(HttpClient http)
{
    public string? Token { get; private set; }
    public Guid UserId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public string Email { get; private set; } = "";
    public HttpClient Http => http;

    public static async Task<Api> RegisterAsync(ApiFactory f, string? email = null, string password = "correct-horse-battery")
    {
        var api = new Api(f.CreateClient());
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        var r = await api.PostAsync("/api/auth/register", new { email, password, displayName = "Test Kullanıcı" });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        api.Token = r.Str("token");
        api.Email = email;
        api.UserId = Guid.Parse(r["user"].GetProperty("id").GetString()!);
        api.WorkspaceId = Guid.Parse(r["workspaces"][0].GetProperty("id").GetString()!);
        api.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        return api;
    }

    public Task<Resp> GetAsync(string url) => SendAsync(HttpMethod.Get, url, null);
    public Task<Resp> PostAsync(string url, object? body = null, string? idempotencyKey = null) => SendAsync(HttpMethod.Post, url, body ?? new { }, idempotencyKey);
    public Task<Resp> PatchAsync(string url, object body) => SendAsync(HttpMethod.Patch, url, body);
    public Task<Resp> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url, null);

    private async Task<Resp> SendAsync(HttpMethod method, string url, object? body, string? idempotencyKey = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");
        if (idempotencyKey is not null) req.Headers.Add("Idempotency-Key", idempotencyKey);
        using var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        var doc = string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("null") : JsonDocument.Parse(text);
        return new Resp(res.StatusCode, doc.RootElement.Clone());
    }

    // ---- kısayollar
    public async Task<Guid> CreateSampleAsync()
    {
        var r = await PostAsync("/api/samples/expense-management", new { workspaceId = WorkspaceId });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return Guid.Parse(r["summary"].GetProperty("id").GetString()!);
    }

    public async Task<Guid> CreateProjectAsync(string? idea = null, string name = "Test Projesi")
    {
        var r = await PostAsync("/api/projects", new { workspaceId = WorkspaceId, name, description = "açıklama", techProfileId = "aspnetcore", ideaText = idea });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return Guid.Parse(r["summary"].GetProperty("id").GetString()!);
    }

    public async Task<Guid> PendingProposalAsync(Guid projectId)
    {
        var r = await GetAsync($"/api/projects/{projectId}/proposals?status=pending");
        return Guid.Parse(r.Body[0].GetProperty("id").GetString()!);
    }

    public async Task<int> HeadAsync(Guid projectId) =>
        (await GetAsync($"/api/projects/{projectId}")).Body.GetProperty("summary").GetProperty("headNumber").GetInt32();

    public async Task<int> VersionCountAsync(Guid projectId) => (await GetAsync($"/api/projects/{projectId}/versions")).Body.GetArrayLength();

    public async Task AddMemberAsync(Api other, string role, bool? canAccept = null)
    {
        var r = await PostAsync($"/api/workspaces/{WorkspaceId}/members", new { email = other.Email, role, canAccept });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
    }
}
