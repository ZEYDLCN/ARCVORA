using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
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

/// <summary>Muhasebe webhook'unu taklit eder: gelen çağrıları kaydeder; durum kodu veya hata ayarlanabilir.</summary>
public sealed class FakeWebhook : HttpMessageHandler
{
    public sealed record Call(string Url, string Body, IReadOnlyDictionary<string, string> Headers);

    private readonly List<Call> _calls = [];
    public volatile int StatusCode = 200;
    public volatile int DelayMs;
    public Exception? Throw { get; set; }
    public IReadOnlyList<Call> Calls { get { lock (_calls) return [.. _calls]; } }

    public void Reset() { lock (_calls) _calls.Clear(); StatusCode = 200; Throw = null; DelayMs = 0; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (_calls) _calls.Add(new Call(request.RequestUri!.ToString(), body, headers));
        if (DelayMs > 0) await Task.Delay(DelayMs, cancellationToken);
        if (Throw is { } ex) throw ex;
        return new HttpResponseMessage((HttpStatusCode)StatusCode);
    }
}

/// <summary>Gönderilen e-postaları toplar; hata enjekte edilebilir. Gerçek SMTP yolu için <see cref="SmtpSink"/> kullanılır.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<OutgoingMail> _sent = [];
    private readonly Queue<Exception> _failures = new();
    public IReadOnlyList<OutgoingMail> Sent { get { lock (_sent) return [.. _sent]; } }
    public IReadOnlyList<OutgoingMail> To(string address) => [.. Sent.Where(m => string.Equals(m.ToAddress, address, StringComparison.OrdinalIgnoreCase))];
    public void FailNext(params Exception[] errors) { lock (_failures) foreach (var e in errors) _failures.Enqueue(e); }
    public void Reset() { lock (_sent) _sent.Clear(); lock (_failures) _failures.Clear(); }
    public int DelayMs { get; set; }

    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
        Exception? failure = null;
        lock (_failures) if (_failures.Count > 0) failure = _failures.Dequeue();
        if (failure is not null) throw failure;
        lock (_sent) _sent.Add(mail);
    }
}

/// <summary>Her test sınıfı kendi geçici PostgreSQL veritabanını alır.</summary>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static string Admin => Environment.GetEnvironmentVariable("ARCVORA_TEST_PG")
                                   ?? "Host=localhost;Username=postgres;Password=postgres";
    private readonly string _db = "arcvora_test_" + Guid.NewGuid().ToString("N");
    public SwitchableGenerator Generator { get; } = new();
    public FakeWebhook Webhook { get; } = new();
    public FakeEmailSender Mail { get; } = new();
    /// <summary>Alt sınıf true yaparsa gerçek <see cref="SmtpEmailSender"/> kullanılır (yerel SMTP alıcısına karşı).</summary>
    protected virtual bool UseRealSmtp => false;
    public string ConnectionString => $"{Admin};Database={_db}";
    /// <summary>Alt sınıflar belirli bir testte yapılandırmayı değiştirir (ör. disk deposu).</summary>
    protected virtual IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>();

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
            ["Accounting:RetryBaseSeconds"] = "0",      // yeniden denemeler beklemeden, deterministik
            ["Accounting:MaxAttempts"] = "3",
            ["Email:Enabled"] = "true", ["Email:Host"] = "smtp.test", ["Email:From"] = "noreply@firma.test", ["Email:PublicUrl"] = "https://apps.test",
            ["Email:RetryBaseSeconds"] = "0", ["Email:MaxAttempts"] = "3",
            ["Attachments:StagedTtlHours"] = "0",       // temizlik testi beklemeden çalışsın; bağlama süre dolumuna bakmaz
        };
        foreach (var (k, v) in ExtraSettings) settings[k] = v;
        foreach (var (k, v) in settings) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IArchitectureGenerator>();
            s.AddSingleton<IArchitectureGenerator>(Generator);
            s.AddHttpClient(AccountingDispatcher.ClientName).ConfigurePrimaryHttpMessageHandler(() => Webhook);
            if (!UseRealSmtp) { s.RemoveAll<IEmailSender>(); s.AddSingleton<IEmailSender>(Mail); }
        });
    }

    /// <summary>Muhasebe kuyruğunda gönderilecek kayıt kalmayana kadar çalıştırır; yapılan deneme sayısını döndürür.</summary>
    public async Task<int> RunAccountingAsync(int max = 20)
    {
        var d = Services.GetRequiredService<AccountingDispatcher>();
        var n = 0;
        while (n < max && await d.RunOnceAsync(CancellationToken.None)) n++;
        return n;
    }

    /// <summary>E-posta kuyruğunda gönderilecek kayıt kalmayana kadar çalıştırır; işlenen kayıt sayısını döndürür.</summary>
    public async Task<int> RunEmailAsync(int max = 200)
    {
        var d = Services.GetRequiredService<EmailDispatcher>();
        var n = 0;
        while (n < max && await d.RunOnceAsync(CancellationToken.None)) n++;
        return n;
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

    /// <summary>Var olan bir hesapla giriş yapar (yönetici tarafından açılmış kullanıcılar için).</summary>
    public static async Task<Api> LoginAsync(ApiFactory f, string email, string password, Guid? workspaceId = null)
    {
        var api = new Api(f.CreateClient());
        var r = await api.PostAsync("/api/auth/login", new { email, password });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        api.Token = r.Str("token");
        api.Email = email;
        api.UserId = Guid.Parse(r["user"].GetProperty("id").GetString()!);
        api.WorkspaceId = workspaceId ?? Guid.Parse(r["workspaces"][0].GetProperty("id").GetString()!);
        api.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", api.Token);
        return api;
    }

    /// <summary>Yönetici olarak yeni kullanıcı açar (yanıtı döndürür).</summary>
    public Task<Resp> CreateUserAsync(string? email = null, string role = "app_user", string password = "temporary-pass-1",
        string displayName = "Çalışan", Guid? managerId = null) =>
        PostAsync($"/api/workspaces/{WorkspaceId}/users",
            new { email = email ?? $"u-{Guid.NewGuid():N}@example.com", displayName, password, role, managerId });

    /// <summary>Yönetici olarak kullanıcı açar ve o kullanıcı olarak giriş yapar.</summary>
    public async Task<Api> NewUserAsync(ApiFactory f, string role = "app_user", string password = "temporary-pass-1",
        string displayName = "Çalışan", Guid? managerId = null)
    {
        var email = $"u-{Guid.NewGuid():N}@example.com";
        var r = await CreateUserAsync(email, role, password, displayName, managerId);
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return await LoginAsync(f, email, password, WorkspaceId);
    }

    public Task<Resp> GetAsync(string url) => SendAsync(HttpMethod.Get, url, null);
    public Task<Resp> PostAsync(string url, object? body = null, string? idempotencyKey = null) => SendAsync(HttpMethod.Post, url, body ?? new { }, idempotencyKey);
    public Task<Resp> PatchAsync(string url, object body) => SendAsync(HttpMethod.Patch, url, body);
    public Task<Resp> PutAsync(string url, object body) => SendAsync(HttpMethod.Put, url, body);
    public Task<Resp> DeleteAsync(string url) => SendAsync(HttpMethod.Delete, url, null);

    /// <summary>Ham gövdeyle dosya yükler (<c>POST /apps/{id}/uploads?fileName=</c>).</summary>
    public async Task<Resp> UploadAsync(Guid appId, byte[] bytes, string? fileName = "fis.png")
    {
        var url = $"/api/apps/{appId}/uploads" + (fileName is null ? "" : "?fileName=" + Uri.EscapeDataString(fileName));
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return new Resp(res.StatusCode, string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("null").RootElement.Clone() : JsonDocument.Parse(text).RootElement.Clone());
    }

    /// <summary>İkili yanıt (dosya) indirir; başlıklar için ham yanıtı da döndürür.</summary>
    public async Task<(HttpStatusCode Status, byte[] Bytes, HttpResponseMessage Response)> DownloadAsync(string url)
    {
        var res = await http.GetAsync(url);
        return (res.StatusCode, await res.Content.ReadAsByteArrayAsync(), res);
    }

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
