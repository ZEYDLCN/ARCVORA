using System.Net;
using System.Text.Json;
using static Arcvora.Integration.Tests.AppsKit;

namespace Arcvora.Integration.Tests;

/// <summary>
/// Arayüzdeki hazır şablonlar (web/src/lib/templates.json) gerçek sunucu doğrulayıcısından geçer, yayınlanır ve beklenen akışı izler.
/// Arayüz testleri yalnızca istemci aynasını sınar; burada tek kaynak olan JSON sunucuya verilir.
/// </summary>
public class TemplatesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static JsonElement Templates()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arcvora.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "web", "src", "lib", "templates.json")));
        return doc.RootElement.Clone();
    }

    private static JsonElement Template(string id) => Templates().EnumerateArray().First(t => t.GetProperty("id").GetString() == id);

    private async Task<(Api Admin, Api Boss, Api Worker, Guid App)> SetupAsync(string template)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Yönetici");
        var worker = await admin.NewUserAsync(factory, displayName: "Çalışan", managerId: boss.UserId);
        var t = Template(template);
        var def = t.GetProperty("definition");
        var created = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/apps", new { name = t.GetProperty("name").GetString(), icon = t.GetProperty("icon").GetString(), definition = def });
        Assert.True(created.Is(HttpStatusCode.Created), created.ToString());
        var app = Guid.Parse(created["summary"].GetProperty("id").GetString()!);
        Assert.Equal(0, created["issues"].GetArrayLength());
        await admin.PublishAsync(app);
        await admin.GrantAsync(app, [worker.UserId]);
        return (admin, boss, worker, app);
    }

    [Fact]
    public void The_template_file_lists_the_three_flows_with_unique_ids_and_prefixes()
    {
        var all = Templates().EnumerateArray().ToList();
        Assert.Equal(["advance", "expense", "leave"], all.Select(t => t.GetProperty("id").GetString()!).Order().ToArray());
        Assert.Equal(3, all.Select(t => t.GetProperty("definition").GetProperty("prefix").GetString()).Distinct().Count());
    }

    [Theory]
    [InlineData("leave")]
    [InlineData("advance")]
    [InlineData("expense")]
    public async Task Each_template_publishes_on_the_real_server(string id)
    {
        var (admin, _, _, app) = await SetupAsync(id);
        var detail = await admin.GetAsync($"/api/apps/{app}");
        Assert.Equal("published", detail["summary"].GetProperty("status").GetString());
        Assert.Equal(0, detail["issues"].GetArrayLength());
    }

    [Fact]
    public async Task Leave_needs_only_the_manager_up_to_ten_days_and_the_master_admin_beyond()
    {
        var (admin, boss, worker, app) = await SetupAsync("leave");

        var shortLeave = await worker.SubmitAsync(app, new { kind = "Yıllık izin", start = "2026-11-10", end = "2026-11-14" });
        Assert.True(shortLeave.Is(HttpStatusCode.Created), shortLeave.ToString());
        Assert.Equal(5, shortLeave["data"].GetProperty("days").GetInt32());
        var id1 = Guid.Parse(shortLeave["summary"].GetProperty("id").GetString()!);
        var done = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id1)}/approve");
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/entries")).Body.GetArrayLength());     // izin muhasebeye gitmez

        var longLeave = await worker.SubmitAsync(app, new { kind = "Mazeret izni", start = "2026-12-01", end = "2026-12-12" });      // 12 gün
        var id2 = Guid.Parse(longLeave["summary"].GetProperty("id").GetString()!);
        var afterBoss = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id2)}/approve");
        Assert.Equal("in_review", afterBoss["summary"].GetProperty("status").GetString());
        Assert.StartsWith("Master admin onayı", afterBoss["summary"].GetProperty("currentStepName").GetString());
        var final = await admin.PostAsync($"/api/tasks/{await admin.MyPendingTaskAsync(id2)}/approve");
        Assert.Equal("approved", final["summary"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Leave_rejects_an_end_date_before_the_start_and_an_unknown_kind()
    {
        var (_, _, worker, app) = await SetupAsync("leave");
        var r = await worker.SubmitAsync(app, new { kind = "Hediye", start = "2026-11-14", end = "2026-11-10" });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        var codes = r["details"].EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToArray();
        Assert.Contains("invalid_option", codes);
        Assert.Contains("not_before", codes);
    }

    [Fact]
    public async Task Advance_above_ten_thousand_needs_the_master_admin_and_always_reaches_accounting()
    {
        var (admin, boss, worker, app) = await SetupAsync("advance");

        var small = await worker.SubmitAsync(app, new { amount = 8000, reason = "Kira" });
        var smallId = Guid.Parse(small["summary"].GetProperty("id").GetString()!);
        Assert.Equal("approved", (await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(smallId)}/approve"))["summary"].GetProperty("status").GetString());

        var big = await worker.SubmitAsync(app, new { amount = 12000.50m, reason = "Taşınma" });
        var bigId = Guid.Parse(big["summary"].GetProperty("id").GetString()!);
        var afterBoss = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(bigId)}/approve");
        Assert.Equal("in_review", afterBoss["summary"].GetProperty("status").GetString());
        Assert.Equal("approved", (await admin.PostAsync($"/api/tasks/{await admin.MyPendingTaskAsync(bigId)}/approve"))["summary"].GetProperty("status").GetString());

        var entries = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/entries");
        Assert.Equal(2, entries.Body.GetArrayLength());
        Assert.All(entries.Body.EnumerateArray(), e => Assert.Equal("avans", e.GetProperty("category").GetString()));
    }

    [Fact]
    public async Task Expense_validates_amounts_categories_and_queues_accounting_after_approval()
    {
        var (admin, boss, worker, app) = await SetupAsync("expense");
        Assert.Contains("out_of_range", (await worker.SubmitAsync(app, new { expense_date = "2026-11-03", amount = 0, category = "Yemek", description = "x" }))["details"]
            .EnumerateArray().Select(i => i.GetProperty("code").GetString()));
        Assert.Contains("invalid_option", (await worker.SubmitAsync(app, new { expense_date = "2026-11-03", amount = 10, category = "Lüks", description = "x" }))["details"]
            .EnumerateArray().Select(i => i.GetProperty("code").GetString()));

        var ok = await worker.SubmitAsync(app, new { expense_date = "2026-11-03", amount = 120.5m, category = "Yemek", description = "Öğle yemeği", receipt_no = "F-2026-77" });
        Assert.True(ok.Is(HttpStatusCode.Created), ok.ToString());
        var id = Guid.Parse(ok["summary"].GetProperty("id").GetString()!);
        Assert.Equal("approved", (await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id)}/approve"))["summary"].GetProperty("status").GetString());
        var entry = (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/entries")).Body[0];
        Assert.Equal("masraf", entry.GetProperty("category").GetString());
        Assert.Contains("F-2026-77", entry.GetProperty("payload").GetProperty("fields").ToString());
    }
}
