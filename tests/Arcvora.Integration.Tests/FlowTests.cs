using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arcvora.Core.Ai;

namespace Arcvora.Integration.Tests;

public class FlowTests(ApiFactory factory) : IClassFixture<ApiFactory>, IDisposable
{
    private const string Idea =
        "Şirketlerin çalışan masraflarını yönettiği bir SaaS geliştireceğiz. Çalışanlar fiş yükleyip masraf girecek. " +
        "Yönetici onaylayacak. Onaylanan masraf muhasebe sistemine gönderilecek. Her şirket yalnızca kendi verisini görecek. " +
        "ASP.NET Core kullanacağız. Küçük bir ekibiz.";

    public void Dispose() => factory.Generator.Reset();

    private async Task<Resp> RunAndAccept(Api u, Guid pid, string path, object? body = null)
    {
        var job = await u.PostAsync(path, body);
        Assert.True(job.Is(HttpStatusCode.Accepted), job.ToString());
        await factory.RunJobsAsync();
        var done = await u.GetAsync($"/api/jobs/{job.Str("id")}");
        Assert.Equal("succeeded", done.Str("status"));
        var proposalId = done.Str("proposalId");
        var accepted = await u.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/accept", new { });
        Assert.True(accepted.Is(HttpStatusCode.OK), accepted.ToString());
        return accepted;
    }

    [Fact]
    public async Task Idea_to_requirements_to_architecture_to_handoff_end_to_end_with_the_heuristic_provider()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateProjectAsync(Idea, "Masraf Uygulaması");

        // 1) Gereksinim çıkarımı -> inceleme -> kabul
        var job = await u.PostAsync($"/api/projects/{pid}/analyses");
        await factory.RunJobsAsync();
        var proposalId = (await u.GetAsync($"/api/jobs/{job.Str("id")}")).Str("proposalId");
        var proposal = await u.GetAsync($"/api/projects/{pid}/proposals/{proposalId}");
        Assert.False(proposal["summary"].GetProperty("isAi").GetBoolean());                 // AI olmadığı açık
        Assert.Equal("heuristic", proposal["summary"].GetProperty("provider").GetString());
        Assert.Contains("yapay zekâ ile değil", proposal["summary"].GetProperty("note").GetString());
        Assert.True(proposal["patch"].GetProperty("operations").GetArrayLength() >= 5);

        var a1 = await u.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/accept", new { });
        Assert.Equal(2, a1["version"].GetProperty("number").GetInt32());
        var v2 = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"];
        Assert.True(v2.GetProperty("requirements").GetArrayLength() >= 4);
        Assert.All(v2.GetProperty("requirements").EnumerateArray(), r =>
        {
            Assert.Equal("user_statement", r.GetProperty("origin").GetString());
            Assert.Equal("confirmed", r.GetProperty("reviewStatus").GetString());           // kabul ile onaylandı
            Assert.Equal("SRC-001", r.GetProperty("sourceRefs")[0].GetProperty("sourceId").GetString());
        });
        Assert.InRange(v2.GetProperty("openQuestions").GetArrayLength(), 1, 3);

        // Aynı kaynak ikinci kez analiz edilemez
        var again = await u.PostAsync($"/api/projects/{pid}/analyses");
        Assert.Equal("no_pending_sources", again.Str("code"));

        // 2) Mimari taslak -> kabul
        var a2 = await RunAndAccept(u, pid, $"/api/projects/{pid}/architecture-proposals");
        Assert.Equal(3, a2["version"].GetProperty("number").GetInt32());

        // 3) Aynı model sürümünden üç görünüm
        var views = await u.GetAsync($"/api/projects/{pid}/versions/3/views");
        var kinds = views["views"].EnumerateArray().Select(v => v.GetProperty("kind").GetString()).ToList();
        Assert.Contains("component", kinds);
        Assert.Contains("er", kinds);
        var comp = views["views"].EnumerateArray().First(v => v.GetProperty("kind").GetString() == "component").GetProperty("mermaid").GetString()!;
        Assert.StartsWith("flowchart TD", comp);
        Assert.Contains("Uygulama API", comp);

        // 4) Doğrulama: engelleyici hata yok; boşluklar (kabul ölçütü) görünür
        var findings = await u.GetAsync($"/api/projects/{pid}/versions/3/findings");
        Assert.DoesNotContain(findings["findings"].EnumerateArray(), f => f.GetProperty("severity").GetString() == "error");
        Assert.Contains(findings["findings"].EnumerateArray(), f => f.GetProperty("ruleId").GetString() == "TRC-004");

        // 5) Teknik devir
        var export = await u.PostAsync($"/api/projects/{pid}/exports", new { versionNumber = 3, format = "markdown" });
        Assert.True(export.Is(HttpStatusCode.Created), export.ToString());
        Assert.Equal(3, export["versionNumber"].GetInt32());
        Assert.Equal("aspnetcore", export.Str("profileId"));
        Assert.Equal("arcvora-handoff/1", export.Str("generatorVersion"));
        var versionHash = (await u.GetAsync($"/api/projects/{pid}/versions/3"))["info"].GetProperty("contentHash").GetString();
        Assert.Equal(versionHash, export.Str("modelHash"));                                  // çıktı hangi tasarıma ait: izlenebilir

        var res = await u.Http.GetAsync($"/api/projects/{pid}/exports/{export.Str("id")}/download");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition!.ToString());
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(export.Str("contentHash"), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var md = Encoding.UTF8.GetString(bytes);
        Assert.Contains("# Masraf Uygulaması — Teknik Devir", md);
        Assert.Contains("```mermaid\nflowchart TD", md);
        Assert.Contains("üretime hazır olduğu doğrulandı", md);
        Assert.Contains("Gereksinim çıkarımı", md);                                          // sürüm geçmişi
    }

    [Fact]
    public async Task Change_request_with_llm_produces_impact_and_a_new_version_while_the_old_one_stays_reachable()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var samplePending = await u.PendingProposalAsync(pid);
        await u.PostAsync($"/api/projects/{pid}/proposals/{samplePending}/reject", new { });

        var draft = new
        {
            summary = "Onayda e-posta bildirimi",
            groups = new[] { new { id = "g-notify", title = "Bildirim", description = "", objectIds = new[] { "new-1", "new-2", "new-3", "new-4", "new-5" } } },
            requirements = new object[]
            {
                new
                {
                    id = "new-1", text = "Onaylanan masraf için çalışana e-posta gitmeli", kind = "functional", priority = "medium", origin = "user_statement",
                    sourceRefs = new[] { new { sourceId = "SRC-002", excerpt = "Onayda e-posta bildirimi gitsin" } },
                    acceptanceCriteria = new[] { new { id = "new-ac-1", text = "Onay sonrası çalışana bildirim gönderilir" } },
                },
            },
            components = new object[] { new { id = "new-2", name = "Bildirim servisi", kind = "module", responsibility = "E-posta gönderimi" } },
            relationships = new object[] { new { id = "new-3", sourceId = "CMP-006", targetId = "new-2", communication = "async", description = "Bildirim olayı", failureBehavior = "Yeniden denenir; onayı engellemez" } },
            decisions = new object[] { new { id = "new-4", title = "Bildirimler asenkron gönderilir", choice = "Worker üzerinden", rationale = "Onay işlemini bloklamamak" } },
            traceLinks = new object[]
            {
                new { id = "new-5", fromId = "new-4", toId = "new-1", kind = "addresses" },
            },
            possibleImpacts = new object[] { new { objectId = "REQ-006", reason = "Onay sonrası davranış genişliyor" } },
        };
        factory.Generator.Current = new LlmArchitectureGenerator(new ScriptedLlm(draft));

        var job = await u.PostAsync($"/api/projects/{pid}/change-requests", new { requestText = "Onayda e-posta bildirimi gitsin" });
        await factory.RunJobsAsync();
        var done = await u.GetAsync($"/api/jobs/{job.Str("id")}");
        Assert.Equal("succeeded", done.Str("status"));
        Assert.True(done["tokensUsed"].GetInt32() > 0);

        var proposal = await u.GetAsync($"/api/projects/{pid}/proposals/{done.Str("proposalId")}");
        Assert.True(proposal["summary"].GetProperty("isAi").GetBoolean());
        var items = proposal["impact"].GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("id").GetString() == "REQ-010" && i.GetProperty("level").GetString() == "direct");
        Assert.Contains(items, i => i.GetProperty("id").GetString() == "REQ-006" && i.GetProperty("level").GetString() == "possible");
        Assert.Contains(items, i => i.GetProperty("id").GetString() == "SRC-002" && i.GetProperty("level").GetString() == "direct");   // talep kaynak olarak kaydedildi

        // Üretici 'confirmed' ve 'user_statement' iddiasında bulunsa da yeni nesneler önerilmiş durumda; alıntı doğrulandığı için kullanıcı ifadesi
        var ops = proposal["patch"].GetProperty("operations").EnumerateArray().ToList();
        var comp = ops.First(o => o.GetProperty("type").GetString() == "component").GetProperty("value");
        Assert.Equal("proposed", comp.GetProperty("reviewStatus").GetString());
        var req = ops.First(o => o.GetProperty("type").GetString() == "requirement").GetProperty("value");
        Assert.Equal("user_statement", req.GetProperty("origin").GetString());

        var before = (await u.GetAsync($"/api/projects/{pid}/versions/1"))["info"].GetProperty("contentHash").GetString();
        var accepted = await u.PostAsync($"/api/projects/{pid}/proposals/{done.Str("proposalId")}/accept", new { });
        Assert.True(accepted.Is(HttpStatusCode.OK), accepted.ToString());
        Assert.Equal(2, accepted["version"].GetProperty("number").GetInt32());

        var v2 = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"];
        Assert.Contains(v2.GetProperty("components").EnumerateArray(), c => c.GetProperty("name").GetString() == "Bildirim servisi");
        Assert.Contains(v2.GetProperty("sources").EnumerateArray(), s => s.GetProperty("text").GetString() == "Onayda e-posta bildirimi gitsin");
        Assert.Equal(before, (await u.GetAsync($"/api/projects/{pid}/versions/1"))["info"].GetProperty("contentHash").GetString());
    }

    [Fact]
    public async Task Heuristic_change_request_records_the_request_and_never_invents_design_changes()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var job = await u.PostAsync($"/api/projects/{pid}/change-requests", new { requestText = "Raporlarda masraf onay geçmişi görünsün" });
        await factory.RunJobsAsync();
        var done = await u.GetAsync($"/api/jobs/{job.Str("id")}");
        var proposal = await u.GetAsync($"/api/projects/{pid}/proposals/{done.Str("proposalId")}");

        Assert.Contains("tasarım değişikliği önermez", proposal["summary"].GetProperty("note").GetString());
        var types = proposal["patch"].GetProperty("operations").EnumerateArray().Select(o => o.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { "requirement", "source" }, types.OrderBy(t => t).ToArray());
        Assert.Contains(proposal["impact"].GetProperty("items").EnumerateArray(), i => i.GetProperty("level").GetString() == "possible");
    }

    [Fact]
    public async Task Sample_two_stage_approval_matches_the_documented_demo()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        var d = await u.GetAsync($"/api/projects/{pid}/proposals/{prop}");

        Assert.False(d["summary"].GetProperty("isAi").GetBoolean());
        Assert.Equal("sample", d["summary"].GetProperty("provider").GetString());
        var areas = d["impact"].GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("area").GetString()).ToList();
        foreach (var area in new[] { "Gereksinimler", "Veri modeli", "API", "Akışlar", "Kararlar", "Açık sorular" })
            Assert.Contains(area, areas);
        Assert.Contains("OQ-004", d["impact"].GetProperty("openQuestionIds").EnumerateArray().Select(x => x.GetString()));   // kritik soru görünür

        // Yeni bulgular: açık soruya bağlı karar belirsiz, ama engelleyici hata yok
        Assert.DoesNotContain(d["newFindings"].EnumerateArray(), f => f.GetProperty("severity").GetString() == "error");

        await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        var views = (await u.GetAsync($"/api/projects/{pid}/versions/2/views"))["views"].EnumerateArray().ToList();
        Assert.Contains(views, v => v.TryGetProperty("flowId", out var f) && f.GetString() == "FLW-003");
        var seq = views.First(v => v.TryGetProperty("flowId", out var f) && f.GetString() == "FLW-003").GetProperty("mermaid").GetString()!;
        Assert.Contains("alt Politikada sonraki adım var", seq);
        Assert.Contains("else Son adım", seq);
    }

    [Fact]
    public async Task Exports_are_deterministic_traceable_and_deduplicated()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();

        var a = await u.PostAsync($"/api/projects/{pid}/exports", new { format = "markdown" });
        var b = await u.PostAsync($"/api/projects/{pid}/exports", new { format = "markdown" });
        Assert.Equal(a.Str("id"), b.Str("id"));                                              // aynı sürüm + aynı üretici = aynı çıktı kaydı
        Assert.Equal(a.Str("contentHash"), b.Str("contentHash"));
        Assert.Equal(1, (await u.GetAsync($"/api/projects/{pid}/exports")).Body.GetArrayLength());

        // Model değişince farklı çıktı ve farklı model özeti
        await u.PatchAsync($"/api/projects/{pid}/open-questions/OQ-001", new { answer = "Hayır" });
        var c = await u.PostAsync($"/api/projects/{pid}/exports", new { format = "markdown" });
        Assert.NotEqual(a.Str("modelHash"), c.Str("modelHash"));
        Assert.NotEqual(a.Str("contentHash"), c.Str("contentHash"));
        Assert.Equal(2, c["versionNumber"].GetInt32());

        // Eski sürüm hâlâ aynı içeriği üretir
        var old = await u.PostAsync($"/api/projects/{pid}/exports", new { versionNumber = 1, format = "markdown" });
        Assert.Equal(a.Str("contentHash"), old.Str("contentHash"));
    }

    [Fact]
    public async Task Package_export_is_a_zip_with_the_documented_files()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var export = await u.PostAsync($"/api/projects/{pid}/exports", new { format = "package" });
        Assert.True(export.Is(HttpStatusCode.Created), export.ToString());
        Assert.EndsWith(".zip", export.Str("fileName"));

        var bytes = await (await u.Http.GetAsync($"/api/projects/{pid}/exports/{export.Str("id")}/download")).Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "ARCHITECTURE.md", "CHANGELOG.md", "DECISIONS.md", "MANIFEST.json", "OPEN_QUESTIONS.md", "PROJECT_OVERVIEW.md", "REQUIREMENTS.md" }, names);
        using var manifest = new StreamReader(zip.GetEntry("MANIFEST.json")!.Open());
        Assert.Contains(export.Str("modelHash"), await manifest.ReadToEndAsync());

        var bad = await u.PostAsync($"/api/projects/{pid}/exports", new { format = "docx" });
        Assert.Equal("unknown_format", bad.Str("code"));
        var missing = await u.PostAsync($"/api/projects/{pid}/exports", new { versionNumber = 42 });
        Assert.True(missing.Is(HttpStatusCode.NotFound));
    }
}
