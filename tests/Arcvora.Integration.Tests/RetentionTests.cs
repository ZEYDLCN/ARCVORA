using System.Net;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>G5: denetim kaydı ekranı, saklama süresi ve silme, kişisel veri dışa aktarımı ve anonimleştirme (KVKK).</summary>
public class RetentionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App);

    private async Task<Kit> SetupAsync(bool accounting = false)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Müdür Mehmet");
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(accounting ? AppsKit.ReceiptDef(2, false, true) : AppsKit.LeaveDef(), "Uygulama", emp.UserId);
        return new Kit(admin, boss, emp, app);
    }

    private static string Ws(Api a, string rest) => $"/api/workspaces/{a.WorkspaceId}{rest}";

    private async Task<Guid> ApprovedAsync(Kit k, bool leave = true)
    {
        var r = leave ? await k.Emp.SubmitAsync(k.App, AppsKit.Leave()) : await k.Emp.SubmitAsync(k.App, new { amount = 10m });
        var id = r["summary"].GetProperty("id").GetGuid();
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(id)}/approve");
        return id;
    }

    private async Task AgeAsync(Guid requestId, int months)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "FlowRequests" SET "CompletedAt" = now() - make_interval(months => {months}), "CreatedAt" = now() - make_interval(months => {months}) WHERE "Id" = {requestId}""");
    }

    // ===================================================================== denetim

    [Fact]
    public async Task The_audit_log_lists_actions_with_actor_names_and_filters()
    {
        var k = await SetupAsync();
        await k.Admin.PostAsync(Ws(k.Admin, "/groups"), new { name = "Muhasebe" });
        var all = await k.Admin.GetAsync(Ws(k.Admin, "/audit"));
        Assert.True(all.Is(HttpStatusCode.OK), all.ToString());
        Assert.True(all["total"].GetInt32() >= 4);
        var created = await k.Admin.GetAsync(Ws(k.Admin, "/audit?action=group.created"));
        Assert.Equal(1, created["total"].GetInt32());
        var row = created["items"][0];
        Assert.Equal("group.created", row.GetProperty("action").GetString());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("actorName").GetString()));
        Assert.Equal(k.Admin.UserId.ToString(), row.GetProperty("actorId").GetString());

        Assert.Equal(0, (await k.Admin.GetAsync(Ws(k.Admin, $"/audit?actorId={Guid.NewGuid()}")))["total"].GetInt32());
        var users = await k.Admin.GetAsync(Ws(k.Admin, "/audit?action=user.created"));
        Assert.Equal(2, users["total"].GetInt32());                                   // boss + emp
        var paged = await k.Admin.GetAsync(Ws(k.Admin, "/audit?pageSize=2&page=2"));
        Assert.True(paged["items"].GetArrayLength() <= 2);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));
        Assert.True((await k.Admin.GetAsync(Ws(k.Admin, $"/audit?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}")))["total"].GetInt32() >= 4);
        Assert.Equal(0, (await k.Admin.GetAsync(Ws(k.Admin, $"/audit?to={today.AddDays(-1):yyyy-MM-dd}")))["total"].GetInt32());
    }

    [Fact]
    public async Task The_audit_log_is_admin_only_workspace_scoped_and_validates_input()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await k.Emp.GetAsync(Ws(k.Admin, "/audit"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.GetAsync(Ws(k.Admin, "/audit"))).Is(HttpStatusCode.NotFound));
        var mine = await outsider.GetAsync(Ws(outsider, "/audit"));
        Assert.DoesNotContain(k.Boss.UserId.ToString(), mine.Body.GetRawText());      // başka şirketin kayıtları sızmaz
        foreach (var bad in new[] { "page=0", "pageSize=1000", "from=dün", "from=2026-11-10&to=2026-11-01" })
            Assert.True((await k.Admin.GetAsync(Ws(k.Admin, "/audit?" + bad))).Is(HttpStatusCode.UnprocessableEntity), bad);
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Ws(k.Admin, "/audit"))).StatusCode);
    }

    // ================================================================= saklama süresi

    [Fact]
    public async Task Retention_settings_default_to_keep_forever_and_validate_the_period()
    {
        var k = await SetupAsync();
        var d = await k.Admin.GetAsync(Ws(k.Admin, "/retention"));
        Assert.True(d.Is(HttpStatusCode.OK), d.ToString());
        Assert.False(d.Body.TryGetProperty("closedRequestMonths", out _));
        Assert.False(d["autoPurge"].GetBoolean());

        var set = await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 24, autoPurge = false });
        Assert.True(set.Is(HttpStatusCode.OK), set.ToString());
        Assert.Equal(24, set["closedRequestMonths"].GetInt32());
        foreach (var bad in new[] { 3, 0, -1, 241, 1000 })
            Assert.True((await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = bad })).Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.Equal(24, (await k.Admin.GetAsync(Ws(k.Admin, "/retention")))["closedRequestMonths"].GetInt32());              // hatalı istek değeri bozmaz
        var cleared = await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { clear = true });
        Assert.False(cleared.Body.TryGetProperty("closedRequestMonths", out _));
        Assert.True((await k.Emp.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 })).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Emp.GetAsync(Ws(k.Admin, "/retention"))).Is(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Preview_and_purge_remove_only_old_closed_requests_with_everything_attached()
    {
        var k = await SetupAsync();
        var old1 = await ApprovedAsync(k);
        var old2 = await ApprovedAsync(k);
        var recent = await ApprovedAsync(k);
        var open = (await k.Emp.SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();
        var cancelledOld = (await k.Emp.SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();
        await k.Emp.PostAsync($"/api/requests/{cancelledOld}/cancel");
        await AgeAsync(old1, 14); await AgeAsync(old2, 30); await AgeAsync(cancelledOld, 20);
        await AgeAsync(open, 40);                                                       // açık talep yaşı ne olursa olsun silinmez

        Assert.Equal("retention_not_set", (await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true })).Str("code"));
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 });
        var preview = await k.Admin.PostAsync(Ws(k.Admin, "/retention/preview"));
        Assert.Equal(3, preview["eligible"].GetInt32());
        Assert.Equal(0, preview["skippedAccounting"].GetInt32());

        var noConfirm = await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { });
        Assert.True(noConfirm.Is(HttpStatusCode.UnprocessableEntity), noConfirm.ToString());
        Assert.Equal("confirmation_required", noConfirm.Str("code"));
        Assert.Equal(5, (await k.Admin.GetAsync(Ws(k.Admin, "/requests/search")))["total"].GetInt32());                 // onay verilmeden silinmedi

        var purged = await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true });
        Assert.True(purged.Is(HttpStatusCode.OK), purged.ToString());
        Assert.Equal(3, purged["purged"].GetInt32());
        var left = await k.Admin.GetAsync(Ws(k.Admin, "/requests/search"));
        Assert.Equal(2, left["total"].GetInt32());
        Assert.Equivalent(new[] { recent, open }.Select(g => g.ToString()).ToArray(), left["items"].EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToArray());
        Assert.True((await k.Admin.GetAsync($"/api/requests/{old1}")).Is(HttpStatusCode.NotFound));
        Assert.Equal(0, (await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true }))["purged"].GetInt32());     // ikinci kez: iş yok

        var audit = await k.Admin.GetAsync(Ws(k.Admin, "/audit?action=retention.purged"));
        Assert.Equal(1, audit["total"].GetInt32());
    }

    [Fact]
    public async Task Purging_also_removes_the_files_of_deleted_requests_and_keeps_other_files()
    {
        var k = await SetupAsync(accounting: false);
        var receipts = await k.Admin.PublishedAppAsync(AppsKit.ReceiptDef(), "Fişli", k.Emp.UserId);
        async Task<(Guid Request, string File)> WithFile()
        {
            var f = (await k.Emp.UploadAsync(receipts, FilesKit.Png())).Str("id");
            var r = (await k.Emp.SubmitAsync(receipts, new { amount = 5m, receipt = new[] { f } }))["summary"].GetProperty("id").GetGuid();
            await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(r)}/approve");
            return (r, f);
        }
        var old = await WithFile();
        var fresh = await WithFile();
        await AgeAsync(old.Request, 30);
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 });
        await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true });

        Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync($"/api/attachments/{old.File}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{fresh.File}")).Status);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        Assert.Equal(0, await db.AttachmentBlobs.CountAsync(b => b.AttachmentId == Guid.Parse(old.File)));
    }

    [Fact]
    public async Task Requests_whose_accounting_record_is_still_open_are_never_purged()
    {
        var k = await SetupAsync(accounting: true);
        var unprocessed = await ApprovedAsync(k, leave: false);
        var processed = await ApprovedAsync(k, leave: false);
        var entries = await k.Admin.GetAsync(Ws(k.Admin, "/accounting/entries"));
        var processedEntry = entries.Body.EnumerateArray().First(e => e.GetProperty("requestId").GetGuid() == processed).GetProperty("id").GetString();
        await k.Admin.PostAsync(Ws(k.Admin, $"/accounting/entries/{processedEntry}/process"), new { note = "girildi" });
        await AgeAsync(unprocessed, 30); await AgeAsync(processed, 30);
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 });

        var preview = await k.Admin.PostAsync(Ws(k.Admin, "/retention/preview"));
        Assert.Equal(1, preview["eligible"].GetInt32());
        Assert.Equal(1, preview["skippedAccounting"].GetInt32());
        var purged = await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true });
        Assert.Equal(1, purged["purged"].GetInt32());
        Assert.Equal(1, purged["skipped"].GetInt32());
        Assert.True((await k.Admin.GetAsync($"/api/requests/{unprocessed}")).Is(HttpStatusCode.OK));           // muhasebe işlemedi: korunur
    }

    [Fact]
    public async Task Automatic_purge_runs_in_maintenance_only_when_switched_on()
    {
        var k = await SetupAsync();
        var old = await ApprovedAsync(k);
        await AgeAsync(old, 30);
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12, autoPurge = false });
        var maintenance = factory.Services.GetRequiredService<MaintenanceService>();
        await maintenance.RunOnceAsync(CancellationToken.None);
        Assert.True((await k.Admin.GetAsync($"/api/requests/{old}")).Is(HttpStatusCode.OK));

        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { autoPurge = true });
        var result = await maintenance.RunOnceAsync(CancellationToken.None);
        Assert.True(result.RequestsPurged >= 1);
        Assert.True((await k.Admin.GetAsync($"/api/requests/{old}")).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Auto_purge_without_a_period_is_refused_and_other_workspaces_are_untouched()
    {
        var k = await SetupAsync();
        var other = await SetupAsync();
        var theirs = await ApprovedAsync(other);
        await AgeAsync(theirs, 60);
        var r = await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { autoPurge = true });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("retention_not_set", r.Str("code"));

        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 6, autoPurge = true });
        await factory.Services.GetRequiredService<MaintenanceService>().RunOnceAsync(CancellationToken.None);
        Assert.True((await other.Admin.GetAsync($"/api/requests/{theirs}")).Is(HttpStatusCode.OK));              // başka çalışma alanının politikası yok → silinmez
        Assert.True((await k.Admin.PostAsync(Ws(other.Admin, "/retention/purge"), new { confirm = true })).Is(HttpStatusCode.NotFound));
    }

    // ================================================================= tek talep silme

    [Fact]
    public async Task An_admin_can_delete_one_closed_request_but_not_an_open_one()
    {
        var k = await SetupAsync();
        var done = await ApprovedAsync(k);
        var open = (await k.Emp.SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();
        var outsider = await Api.RegisterAsync(factory);

        Assert.True((await k.Emp.DeleteAsync($"/api/requests/{done}")).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.DeleteAsync($"/api/requests/{done}")).Is(HttpStatusCode.NotFound));
        var openDel = await k.Admin.DeleteAsync($"/api/requests/{open}");
        Assert.True(openDel.Is(HttpStatusCode.Conflict), openDel.ToString());
        Assert.Equal("request_open", openDel.Str("code"));

        Assert.True((await k.Admin.DeleteAsync($"/api/requests/{done}")).Is(HttpStatusCode.NoContent));
        Assert.True((await k.Admin.GetAsync($"/api/requests/{done}")).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Admin.DeleteAsync($"/api/requests/{done}")).Is(HttpStatusCode.NotFound));
        Assert.Equal(1, (await k.Admin.GetAsync(Ws(k.Admin, "/audit?action=request.deleted")))["total"].GetInt32());
    }

    [Fact]
    public async Task Deleting_a_request_with_an_unprocessed_accounting_record_is_refused()
    {
        var k = await SetupAsync(accounting: true);
        var r = await ApprovedAsync(k, leave: false);
        var del = await k.Admin.DeleteAsync($"/api/requests/{r}");
        Assert.True(del.Is(HttpStatusCode.Conflict), del.ToString());
        Assert.Equal("accounting_unprocessed", del.Str("code"));
        var entry = (await k.Admin.GetAsync(Ws(k.Admin, "/accounting/entries"))).Body[0].GetProperty("id").GetString();
        await k.Admin.PostAsync(Ws(k.Admin, $"/accounting/entries/{entry}/process"));
        Assert.True((await k.Admin.DeleteAsync($"/api/requests/{r}")).Is(HttpStatusCode.NoContent));
    }

    // ============================================================ e-posta kuyruğu temizliği

    [Fact]
    public async Task Old_sent_mail_is_purged_by_maintenance_but_pending_mail_is_kept()
    {
        var k = await SetupAsync();
        await k.Emp.SubmitAsync(k.App, AppsKit.Leave());
        await factory.RunEmailAsync();
        await k.Emp.SubmitAsync(k.App, AppsKit.Leave());                                // yeni: bekleyen
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "EmailMessages" SET "CreatedAt" = now() - interval '200 days' WHERE "WorkspaceId" = {k.Admin.WorkspaceId}""");
        }
        var result = await factory.Services.GetRequiredService<MaintenanceService>().RunOnceAsync(CancellationToken.None);
        Assert.True(result.EmailsPurged >= 1);
        var left = await k.Admin.GetAsync(Ws(k.Admin, "/email/outbox"));
        Assert.All(left.Body.EnumerateArray(), m => Assert.Equal("pending", m.GetProperty("status").GetString()));
    }

    // ================================================================= KVKK: dışa aktarım

    [Fact]
    public async Task A_person_or_an_admin_can_export_all_personal_data_without_secrets()
    {
        var k = await SetupAsync();
        var req = await ApprovedAsync(k);
        await k.Boss.PostAsync(Ws(k.Admin, "/delegations"), new { toUserId = k.Emp.UserId, startsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180)).ToString("yyyy-MM-dd"), endsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180)).AddDays(2).ToString("yyyy-MM-dd") });

        var own = await k.Emp.GetAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/data-export"));
        Assert.True(own.Is(HttpStatusCode.OK), own.ToString());
        Assert.Equal(k.Emp.Email, own["user"].GetProperty("email").GetString());
        Assert.Equal("Çalışan Can", own["user"].GetProperty("displayName").GetString());
        Assert.Equal(1, own["requests"].GetArrayLength());
        Assert.Equal("approved", own["requests"][0].GetProperty("status").GetString());
        Assert.Equal(1, own["delegations"].GetArrayLength());
        Assert.True(own["notifications"].GetProperty("emailEnabled").GetBoolean());

        var bossExport = await k.Admin.GetAsync(Ws(k.Admin, $"/members/{k.Boss.UserId}/data-export"));
        Assert.Equal(1, bossExport["decisions"].GetArrayLength());
        Assert.Equal(req.ToString(), bossExport["decisions"][0].GetProperty("requestId").GetString());

        foreach (var export in new[] { own, bossExport })
        {
            var raw = export.Body.GetRawText();
            Assert.DoesNotContain("pbkdf2", raw);
            Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Data_export_respects_who_may_ask()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await k.Emp.GetAsync(Ws(k.Admin, $"/members/{k.Boss.UserId}/data-export"))).Is(HttpStatusCode.Forbidden));     // başkasının verisi
        Assert.True((await outsider.GetAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/data-export"))).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Admin.GetAsync(Ws(k.Admin, $"/members/{Guid.NewGuid()}/data-export"))).Is(HttpStatusCode.NotFound));
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/data-export"))).StatusCode);
    }

    // ================================================================ KVKK: anonimleştirme

    [Fact]
    public async Task A_removed_user_can_be_anonymised_and_history_keeps_working()
    {
        var k = await SetupAsync();
        var req = await ApprovedAsync(k);
        var email = k.Emp.Email;
        Assert.Equal("still_member", (await k.Admin.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"))).Str("code"));
        await k.Admin.DeleteAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"));

        var done = await k.Admin.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"));
        Assert.True(done.Is(HttpStatusCode.OK), done.ToString());
        var detail = await k.Admin.GetAsync($"/api/requests/{req}");
        Assert.Equal("Silinmiş kullanıcı", detail["summary"].GetProperty("requesterName").GetString());
        Assert.DoesNotContain("Çalışan Can", detail.Body.GetRawText());
        Assert.DoesNotContain(email, (await k.Admin.GetAsync(Ws(k.Admin, "/audit?pageSize=100"))).Body.GetRawText().Replace("anonymized", ""));   // e-posta artık hiçbir yerde okunmaz
        var login = await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email, password = "temporary-pass-1" });
        Assert.True(login.Is(HttpStatusCode.Unauthorized), login.ToString());
        Assert.Equal("already_anonymized", (await k.Admin.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"))).Str("code"));
    }

    [Fact]
    public async Task Only_accounts_this_workspace_created_and_that_belong_to_no_workspace_can_be_anonymised()
    {
        var k = await SetupAsync();
        var self = await Api.RegisterAsync(factory);                                           // kendi kaydıyla açılmış hesap, başka şirketin
        await self.PutAsync($"/api/workspaces/{self.WorkspaceId}/members", new { email = k.Admin.Email, role = "admin" });
        Assert.True((await k.Admin.PostAsync(Ws(k.Admin, $"/members/{self.UserId}/anonymize"))).Status is HttpStatusCode.NotFound or HttpStatusCode.Conflict);
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await outsider.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"))).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Emp.PostAsync(Ws(k.Admin, $"/members/{k.Boss.UserId}/anonymize"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Admin.GetAsync($"/api/requests/{Guid.NewGuid()}")).Is(HttpStatusCode.NotFound));
    }
}
