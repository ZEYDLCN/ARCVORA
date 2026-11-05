using System.Net;
using System.Text.Json;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>G1b: demo (mock) veri — yükleme, kullanılabilirlik, tek tuşla temizleme, gerçek veriye dokunmama.</summary>
public class DemoDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Url(Api a, string rest = "") => $"/api/workspaces/{a.WorkspaceId}/demo-data{rest}";

    private async Task<(Api Admin, Resp Seed)> SeededAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var seed = await admin.PostAsync(Url(admin));
        Assert.True(seed.Is(HttpStatusCode.Created), seed.ToString());
        return (admin, seed);
    }

    private static string[] Statuses(Resp list) => list.Body.EnumerateArray().Select(r => r.GetProperty("status").GetString()!).ToArray();

    private async Task<Api> LoginDemoAsync(Api admin, Resp seed, string namePart)
    {
        var u = seed["users"].EnumerateArray().First(x => x.GetProperty("displayName").GetString()!.Contains(namePart));
        return await Api.LoginAsync(factory, u.GetProperty("email").GetString()!, u.GetProperty("temporaryPassword").GetString()!, admin.WorkspaceId);
    }

    [Fact]
    public async Task Seeding_creates_users_groups_apps_requests_and_files_and_returns_one_time_passwords()
    {
        var (admin, seed) = await SeededAsync();
        Assert.Equal(6, seed["users"].GetArrayLength());
        Assert.Equal(6, seed["users"].EnumerateArray().Select(u => u.GetProperty("temporaryPassword").GetString()).Distinct().Count());
        Assert.All(seed["users"].EnumerateArray(), u =>
        {
            Assert.True(u.GetProperty("temporaryPassword").GetString()!.Length >= 12);
            Assert.EndsWith("@demo.arcvora.example", u.GetProperty("email").GetString());
            Assert.Contains("(demo)", u.GetProperty("displayName").GetString());
        });
        Assert.Equal(1, seed["groups"].GetInt32());
        Assert.Equal(3, seed["apps"].GetInt32());
        Assert.Equal(9, seed["requests"].GetInt32());
        Assert.True(seed["attachments"].GetInt32() >= 4);

        var status = await admin.GetAsync(Url(admin));
        Assert.True(status["present"].GetBoolean());
        Assert.Equal(6, status["users"].GetInt32());
        Assert.Equal(9, status["requests"].GetInt32());

        // Parolalar bir daha gösterilmez.
        Assert.False(status.Body.TryGetProperty("temporaryPassword", out _));
        Assert.DoesNotContain("temporaryPassword", (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members")).Body.GetRawText());
    }

    [Fact]
    public async Task Seeding_twice_is_refused_and_only_master_admins_may_seed()
    {
        var (admin, _) = await SeededAsync();
        var again = await admin.PostAsync(Url(admin));
        Assert.True(again.Is(HttpStatusCode.Conflict), again.ToString());
        Assert.Equal("demo_exists", again.Str("code"));

        var other = await Api.RegisterAsync(factory);
        var worker = await other.NewUserAsync(factory);
        var editor = await other.NewUserAsync(factory, "editor");
        foreach (var who in new[] { worker, editor })
        {
            Assert.True((await who.PostAsync(Url(other))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.GetAsync(Url(other))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.DeleteAsync(Url(other))).Is(HttpStatusCode.Forbidden));
        }
        Assert.True((await other.GetAsync(Url(admin))).Is(HttpStatusCode.NotFound));        // başka çalışma alanı
        Assert.True((await other.PostAsync(Url(admin))).Is(HttpStatusCode.NotFound));
        Assert.True((await other.DeleteAsync(Url(admin))).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Requests_cover_every_state_with_realistic_flow_and_accounting()
    {
        var (admin, _) = await SeededAsync();
        var all = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests");
        var st = Statuses(all);
        Assert.Equal(9, st.Length);
        Assert.True(st.Count(s => s == "in_review") >= 3);
        Assert.True(st.Count(s => s == "approved") >= 3);
        Assert.Equal(1, st.Count(s => s == "rejected"));
        Assert.Equal(1, st.Count(s => s == "cancelled"));
        Assert.All(all.Body.EnumerateArray(), r => Assert.StartsWith("D", r.GetProperty("number").GetString()));      // gerçek numaralarla karışmaz

        var entries = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/entries");
        Assert.True(entries.Body.GetArrayLength() >= 2);                                      // onaylanan avans/masraf muhasebeye düştü
    }

    [Fact]
    public async Task Demo_users_can_log_in_work_their_inbox_and_open_their_receipts()
    {
        var (admin, seed) = await SeededAsync();
        var manager = await LoginDemoAsync(admin, seed, "Mehmet");
        var inbox = await manager.GetAsync("/api/my/tasks");
        Assert.True(inbox.Body.GetArrayLength() >= 2, inbox.ToString());

        var can = await LoginDemoAsync(admin, seed, "Can");
        var mine = await can.GetAsync("/api/my/requests");
        Assert.True(mine.Body.GetArrayLength() >= 3);
        var withFiles = mine.Body.EnumerateArray().Select(r => r.GetProperty("id").GetGuid())
            .Select(async id => await can.GetAsync($"/api/requests/{id}")).Select(t => t.Result).First(d => d["attachments"].GetArrayLength() > 0);
        var att = withFiles["attachments"][0];
        var dl = await can.DownloadAsync($"/api/attachments/{att.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, dl.Status);
        Assert.True(dl.Bytes.Length > 100);

        // Aynı talep PDF olarak yazdırılabilir (gömülü görselle).
        var pdf = await can.DownloadAsync($"/api/requests/{withFiles["summary"].GetProperty("id").GetString()}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.Status);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.Bytes, 0, 5));
    }

    [Fact]
    public async Task The_admin_inbox_receives_the_long_leave_that_needs_master_admin_approval()
    {
        var (admin, _) = await SeededAsync();
        var tasks = await admin.GetAsync("/api/my/tasks");
        Assert.True(tasks.Body.GetArrayLength() >= 1, tasks.ToString());
    }

    [Fact]
    public async Task Removing_demo_data_leaves_nothing_behind_and_is_idempotent()
    {
        var (admin, _) = await SeededAsync();
        Assert.True((await admin.DeleteAsync(Url(admin))).Is(HttpStatusCode.NoContent));

        var status = await admin.GetAsync(Url(admin));
        Assert.False(status["present"].GetBoolean());
        Assert.Equal(0, status["users"].GetInt32() + status["requests"].GetInt32() + status["attachments"].GetInt32() + status["apps"].GetInt32() + status["groups"].GetInt32());
        Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members")).Body.GetArrayLength());       // yalnızca admin
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/groups")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/apps")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/entries")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync("/api/my/tasks")).Body.GetArrayLength());
        Assert.False((await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/accounting/settings")).Body.TryGetProperty("accountingGroupId", out _));

        // Veritabanında da kalıntı yok (ek baytları ve sayaçlar dahil).
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        Assert.Equal(0, await db.Attachments.CountAsync(a => a.WorkspaceId == admin.WorkspaceId));
        Assert.Equal(0, await db.AttachmentBlobs.CountAsync());
        Assert.Equal(0, await db.RequestCounters.CountAsync(c => c.WorkspaceId == admin.WorkspaceId));
        Assert.Equal(0, await db.Users.CountAsync(u => u.IsDemo && u.ManagedByWorkspaceId == admin.WorkspaceId));

        Assert.True((await admin.DeleteAsync(Url(admin))).Is(HttpStatusCode.NoContent));      // ikinci silme zararsız
    }

    [Fact]
    public async Task Demo_can_be_loaded_again_after_removal()
    {
        var (admin, _) = await SeededAsync();
        await admin.DeleteAsync(Url(admin));
        var again = await admin.PostAsync(Url(admin));
        Assert.True(again.Is(HttpStatusCode.Created), again.ToString());
        Assert.Equal(9, again["requests"].GetInt32());
        var numbers = (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests")).Body.EnumerateArray().Select(r => r.GetProperty("number").GetString()).ToList();
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
        Assert.Contains(numbers, n => n!.EndsWith("-0001"));                                 // sayaç temizlendiği için yeniden 0001'den
    }

    [Fact]
    public async Task Real_data_is_never_touched_by_seeding_or_removal()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Gerçek Yönetici");
        var worker = await admin.NewUserAsync(factory, displayName: "Gerçek Çalışan", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "Gerçek İzin", worker.UserId);
        var real = await worker.SubmitAsync(app, AppsKit.Leave());
        Assert.True(real.Is(HttpStatusCode.Created));
        var realNumber = real["summary"].GetProperty("number").GetString();
        Assert.EndsWith("-0001", realNumber);

        Assert.True((await admin.PostAsync(Url(admin))).Is(HttpStatusCode.Created));
        // Demo uygulamaları farklı önek kullandığı için gerçek talep numarası sürer.
        var second = await worker.SubmitAsync(app, AppsKit.Leave("2026-12-01", "2026-12-02"));
        Assert.EndsWith("-0002", second["summary"].GetProperty("number").GetString());

        Assert.True((await admin.DeleteAsync(Url(admin))).Is(HttpStatusCode.NoContent));
        var members = (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members")).Body.EnumerateArray().Select(m => m.GetProperty("displayName").GetString()).ToList();
        Assert.Contains("Gerçek Yönetici", members);
        Assert.Contains("Gerçek Çalışan", members);
        Assert.Equal(3, members.Count);
        Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/apps")).Body.GetArrayLength());
        Assert.Equal(2, (await worker.GetAsync("/api/my/requests")).Body.GetArrayLength());
        Assert.Equal(1, (await boss.GetAsync("/api/my/tasks")).Body.GetArrayLength() - 1);    // yalnızca gerçek görevler (ikisinden biri hâlâ bekliyor)
    }

    [Fact]
    public async Task Removal_is_refused_while_real_requests_depend_on_demo_people()
    {
        var (admin, seed) = await SeededAsync();
        // Yönetici bir demo kullanıcının parolasını sıfırlayıp onunla GERÇEK bir talep açar (demo uygulaması dışında).
        var real = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "Gerçek İzin");
        var demoUser = seed["users"].EnumerateArray().First(u => u.GetProperty("displayName").GetString()!.Contains("Can"));
        await admin.GrantAsync(real, [demoUser.GetProperty("id").GetGuid()]);
        var can = await Api.LoginAsync(factory, demoUser.GetProperty("email").GetString()!, demoUser.GetProperty("temporaryPassword").GetString()!, admin.WorkspaceId);
        Assert.True((await can.SubmitAsync(real, AppsKit.Leave())).Is(HttpStatusCode.Created));

        var del = await admin.DeleteAsync(Url(admin));
        Assert.True(del.Is(HttpStatusCode.Conflict), del.ToString());
        Assert.Equal("demo_in_use", del.Str("code"));
        Assert.True((await admin.GetAsync(Url(admin)))["present"].GetBoolean());              // hiçbir şey silinmedi (kısmi silme yok)
        Assert.Equal(9, (await admin.GetAsync(Url(admin)))["requests"].GetInt32());
    }

    [Fact]
    public async Task Concurrent_seeding_produces_exactly_one_consistent_dataset()
    {
        var admin = await Api.RegisterAsync(factory);
        var results = await Task.WhenAll(admin.PostAsync(Url(admin)), admin.PostAsync(Url(admin)));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.Created)));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.Conflict)));
        var status = await admin.GetAsync(Url(admin));
        Assert.Equal(6, status["users"].GetInt32());
        Assert.Equal(9, status["requests"].GetInt32());
        Assert.Equal(3, status["apps"].GetInt32());
    }

    [Fact]
    public async Task Demo_passwords_force_a_change_and_real_passwords_still_work()
    {
        var (admin, seed) = await SeededAsync();
        var u = seed["users"][0];
        var login = await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email = u.GetProperty("email").GetString(), password = u.GetProperty("temporaryPassword").GetString() });
        Assert.True(login.Is(HttpStatusCode.OK), login.ToString());
        Assert.True(login["user"].GetProperty("mustChangePassword").GetBoolean());
        var bad = await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email = u.GetProperty("email").GetString(), password = "demo123" });
        Assert.True(bad.Is(HttpStatusCode.Unauthorized));
        _ = admin;
    }
}
