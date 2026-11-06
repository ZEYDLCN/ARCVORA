using System.Net;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>G4: e-posta bildirimleri — kuyruk (outbox), olaylar, hatırlatma, tercihler, hata/yeniden deneme ve güvenlik.</summary>
public class EmailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App);

    private async Task<Kit> SetupAsync(string bossName = "Müdür Mehmet", string empName = "Çalışan Can")
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: bossName);
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: empName, managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin Talebi", emp.UserId);
        return new Kit(admin, boss, emp, app);
    }

    private static async Task<Guid> Submit(Kit k) => (await k.Emp.SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();

    private async Task<IReadOnlyList<OutgoingMail>> Delivered(Api who)
    {
        await factory.RunEmailAsync();
        return factory.Mail.To(who.Email);
    }

    private static string Outbox(Api admin, string rest = "") => $"/api/workspaces/{admin.WorkspaceId}/email{rest}";

    private async Task BackdateTasksAsync(Guid requestId, int hours)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ApprovalTasks" SET "CreatedAt" = now() - make_interval(hours => {hours}), "RemindedAt" = CASE WHEN "RemindedAt" IS NULL THEN NULL ELSE now() - make_interval(hours => {hours}) END WHERE "RequestId" = {requestId}""");
    }

    private async Task<int> RemindersAsync()
    {
        var result = await factory.Services.GetRequiredService<MaintenanceService>().RunOnceAsync(CancellationToken.None);
        return result.RemindersQueued;
    }

    // ------------------------------------------------------------------ olaylar

    [Fact]
    public async Task A_new_request_mails_its_approver_through_the_outbox_and_nobody_else()
    {
        var k = await SetupAsync();
        var req = await Submit(k);
        Assert.Empty(factory.Mail.To(k.Boss.Email));                                   // henüz gönderilmedi: kuyrukta

        var queued = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=pending"));
        Assert.True(queued.Is(HttpStatusCode.OK), queued.ToString());
        Assert.Contains(queued.Body.EnumerateArray(), m => m.GetProperty("to").GetString() == k.Boss.Email);

        var mails = await Delivered(k.Boss);
        var mail = Assert.Single(mails);
        Assert.Contains("Onayınız bekleniyor", mail.Subject);
        Assert.Matches(@"IZN-\d{4}-\d{4}", mail.Subject);
        Assert.Equal("Müdür Mehmet", mail.ToName);
        Assert.Contains("Çalışan Can", mail.Text);
        Assert.Contains($"https://apps.test/#/requests/{req}", mail.Text);
        Assert.Contains("Yönetici onayı", mail.Text);
        Assert.Empty(factory.Mail.To(k.Emp.Email));                                    // talep eden kendi talebi için mail almaz
        Assert.Empty(factory.Mail.To(k.Admin.Email));

        await factory.RunEmailAsync();
        Assert.Single(factory.Mail.To(k.Boss.Email));                                  // ikinci tur yeniden göndermez
    }

    [Fact]
    public async Task Approval_rejection_and_a_cancellation_by_someone_else_notify_the_requester()
    {
        var k = await SetupAsync();
        var ok = await Submit(k);
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(ok)}/approve");
        var rejected = await Submit(k);
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(rejected)}/reject", new { comment = "Yoğun dönem <b>maalesef</b>" });
        var cancelled = await Submit(k);
        await k.Admin.PostAsync($"/api/requests/{cancelled}/cancel");
        var selfCancelled = await Submit(k);
        await k.Emp.PostAsync($"/api/requests/{selfCancelled}/cancel");

        var mails = await Delivered(k.Emp);
        Assert.Equal(3, mails.Count);                                                  // kendi iptali için mail yok
        Assert.Contains(mails, m => m.Subject.Contains("onaylandı"));
        var no = Assert.Single(mails, m => m.Subject.Contains("reddedildi"));
        Assert.Contains("Yoğun dönem <b>maalesef</b>", no.Text);                       // düz metinde olduğu gibi
        Assert.DoesNotContain("<b>maalesef</b>", no.Html);                             // HTML'de kaçışlı
        Assert.Contains("&lt;b&gt;maalesef&lt;/b&gt;", no.Html);
        Assert.Contains(mails, m => m.Subject.Contains("iptal"));
    }

    [Fact]
    public async Task Each_step_mails_its_approvers_only_when_it_starts_and_skipped_steps_send_nothing()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür");
        var gm1 = await admin.NewUserAsync(factory, displayName: "GM Bir");
        var gm2 = await admin.NewUserAsync(factory, displayName: "GM İki");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan", managerId: boss.UserId);
        var group = await admin.NewGroupAsync("Genel müdürler", gm1.UserId, gm2.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(Guid.Parse(group.Str("id"))), "İzin", emp.UserId);

        var longLeave = (await emp.SubmitAsync(app, AppsKit.Leave("2026-11-02", "2026-11-20")))["summary"].GetProperty("id").GetGuid();     // 19 gün → GM adımı da çalışır
        await factory.RunEmailAsync();
        Assert.Single(factory.Mail.To(boss.Email));
        Assert.Empty(factory.Mail.To(gm1.Email));                                      // GM adımı henüz başlamadı

        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(longLeave)}/approve");
        await factory.RunEmailAsync();
        Assert.Single(factory.Mail.To(gm1.Email));
        Assert.Single(factory.Mail.To(gm2.Email));                                     // grubun her üyesi

        var shortLeave = (await emp.SubmitAsync(app, AppsKit.Leave("2026-12-01", "2026-12-02")))["summary"].GetProperty("id").GetGuid();   // GM adımı atlanır
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(shortLeave)}/approve");
        await factory.RunEmailAsync();
        Assert.Single(factory.Mail.To(gm1.Email));                                     // yeni mail yok
    }

    [Fact]
    public async Task Reassignment_mails_the_new_approver()
    {
        var k = await SetupAsync();
        var deputy = await k.Admin.NewUserAsync(factory, displayName: "Yeni Onaylayıcı");
        await k.Admin.GrantAsync(k.App, [k.Emp.UserId, deputy.UserId]);
        var req = await Submit(k);
        await factory.RunEmailAsync();
        await k.Admin.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(req)}/reassign", new { toUserId = deputy.UserId });
        var mails = await Delivered(deputy);
        Assert.Single(mails);
        Assert.Contains("Onayınız bekleniyor", mails[0].Subject);
    }

    [Fact]
    public async Task Delegation_mails_the_deputy_and_the_revocation_mails_the_other_party()
    {
        var k = await SetupAsync();
        var deputy = await k.Admin.NewUserAsync(factory, displayName: "Vekil Veli");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));
        var d = await k.Boss.PostAsync($"/api/workspaces/{k.Admin.WorkspaceId}/delegations", new { toUserId = deputy.UserId, startsOn = today.ToString("yyyy-MM-dd"), endsOn = today.AddDays(5).ToString("yyyy-MM-dd"), reason = "Yıllık izin" });
        Assert.True(d.Is(HttpStatusCode.Created), d.ToString());
        var granted = Assert.Single(await Delivered(deputy));
        Assert.Contains("vekil", granted.Subject);
        Assert.Contains("Müdür Mehmet", granted.Text);
        Assert.Contains("Yıllık izin", granted.Text);

        await k.Boss.DeleteAsync($"/api/workspaces/{k.Admin.WorkspaceId}/delegations/{d.Str("id")}");
        var all = await Delivered(deputy);
        Assert.Equal(2, all.Count);
        Assert.Contains("iptal", all[1].Subject);
        Assert.DoesNotContain(factory.Mail.To(k.Boss.Email), m => m.Subject.Contains("Vekâlet"));       // iptal eden kendine mail atmaz
    }

    [Fact]
    public async Task Inviting_a_new_user_mails_the_sign_in_address_but_never_the_password()
    {
        var admin = await Api.RegisterAsync(factory);
        var email = $"yeni-{Guid.NewGuid():N}@example.com";
        var r = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/users", new { email, displayName = "Yeni Kişi", password = "temporary-pass-1", role = "app_user", sendInvite = true });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        await factory.RunEmailAsync();
        var mail = Assert.Single(factory.Mail.To(email));
        Assert.Contains("hesabınız oluşturuldu", mail.Subject.ToLowerInvariant());
        Assert.DoesNotContain("temporary-pass-1", mail.Text + mail.Html);
        Assert.Contains(email, mail.Text);

        var quiet = $"sessiz-{Guid.NewGuid():N}@example.com";
        await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/users", new { email = quiet, displayName = "Sessiz", password = "temporary-pass-1", role = "app_user" });
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(quiet));                                          // davet varsayılan olarak gönderilmez
    }

    // ------------------------------------------------------------------ tercihler

    [Fact]
    public async Task A_user_can_turn_their_notifications_off_and_back_on()
    {
        var k = await SetupAsync();
        Assert.True((await k.Boss.GetAsync("/api/me/notifications"))["emailEnabled"].GetBoolean());
        Assert.True((await k.Boss.PutAsync("/api/me/notifications", new { emailEnabled = false })).Is(HttpStatusCode.OK));
        Assert.False((await k.Boss.GetAsync("/api/me/notifications"))["emailEnabled"].GetBoolean());

        await Submit(k);
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(k.Boss.Email));

        await k.Boss.PutAsync("/api/me/notifications", new { emailEnabled = true });
        await Submit(k);
        Assert.Single(await Delivered(k.Boss));
    }

    [Fact]
    public async Task Turning_notifications_off_after_a_mail_was_queued_still_prevents_sending()
    {
        var k = await SetupAsync();
        await Submit(k);                                                               // kuyrukta
        await k.Boss.PutAsync("/api/me/notifications", new { emailEnabled = false });
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(k.Boss.Email));
        var skipped = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=skipped"));
        Assert.Contains(skipped.Body.EnumerateArray(), m => m.GetProperty("to").GetString() == k.Boss.Email);
    }

    [Fact]
    public async Task A_workspace_can_switch_email_off_entirely_and_only_admins_may()
    {
        var k = await SetupAsync();
        Assert.True((await k.Boss.PutAsync(Outbox(k.Admin), new { emailEnabled = false })).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Boss.GetAsync(Outbox(k.Admin))).Is(HttpStatusCode.Forbidden));
        var off = await k.Admin.PutAsync(Outbox(k.Admin), new { emailEnabled = false });
        Assert.True(off.Is(HttpStatusCode.OK), off.ToString());
        Assert.False(off["emailEnabled"].GetBoolean());
        await Submit(k);
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(k.Boss.Email));
        await k.Admin.PutAsync(Outbox(k.Admin), new { emailEnabled = true });
        await Submit(k);
        Assert.Single(await Delivered(k.Boss));
    }

    [Fact]
    public async Task Settings_report_the_state_without_ever_exposing_credentials()
    {
        var k = await SetupAsync();
        await Submit(k);
        var s = await k.Admin.GetAsync(Outbox(k.Admin));
        Assert.True(s.Is(HttpStatusCode.OK), s.ToString());
        Assert.True(s["configured"].GetBoolean());
        Assert.True(s["emailEnabled"].GetBoolean());
        Assert.True(s["remindersEnabled"].GetBoolean());
        Assert.True(s["pending"].GetInt32() >= 1);
        Assert.Equal("noreply@firma.test", s.Str("from"));
        var text = s.Body.GetRawText().ToLowerInvariant();
        Assert.DoesNotContain("password", text);
        Assert.DoesNotContain("smtp.test", text);
    }

    // ---------------------------------------------------------- hata ve yeniden deneme

    [Fact]
    public async Task Transient_failures_are_retried_until_the_mail_goes_out()
    {
        var k = await SetupAsync();
        factory.Mail.FailNext(new EmailSendException("bağlantı yok", false), new EmailSendException("bağlantı yok", false));
        await Submit(k);
        var mails = await Delivered(k.Boss);
        Assert.Single(mails);
        var sent = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=sent"));
        var row = sent.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == k.Boss.Email);
        Assert.Equal(3, row.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task After_the_maximum_attempts_the_mail_fails_and_an_admin_can_retry_it()
    {
        var k = await SetupAsync();
        factory.Mail.FailNext(new EmailSendException("sunucu kapalı", false), new EmailSendException("sunucu kapalı", false), new EmailSendException("sunucu kapalı", false));
        await Submit(k);
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(k.Boss.Email));
        var failed = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=failed"));
        var row = failed.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == k.Boss.Email);
        Assert.Equal("sunucu kapalı", row.GetProperty("lastError").GetString());
        Assert.Equal(3, row.GetProperty("attempts").GetInt32());

        var id = row.GetProperty("id").GetString();
        Assert.True((await k.Boss.PostAsync(Outbox(k.Admin, $"/outbox/{id}/retry"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Admin.PostAsync(Outbox(k.Admin, $"/outbox/{id}/retry"))).Is(HttpStatusCode.OK));
        Assert.Single(await Delivered(k.Boss));
        Assert.True((await k.Admin.PostAsync(Outbox(k.Admin, $"/outbox/{id}/retry"))).Is(HttpStatusCode.Conflict));      // artık bekleyen/başarısız değil
    }

    [Fact]
    public async Task A_rejected_recipient_fails_at_once_without_retries()
    {
        var k = await SetupAsync();
        factory.Mail.FailNext(new EmailSendException("550 alıcı yok", true));
        await Submit(k);
        await factory.RunEmailAsync();
        var failed = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=failed"));
        var row = failed.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == k.Boss.Email);
        Assert.Equal(1, row.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task Unexpected_exceptions_never_leak_their_message_into_the_stored_error()
    {
        var k = await SetupAsync();
        factory.Mail.FailNext(new InvalidOperationException("kullanıcı=ali parola=SIR-123"), new InvalidOperationException("parola=SIR-123"), new InvalidOperationException("parola=SIR-123"));
        await Submit(k);
        await factory.RunEmailAsync();
        var failed = await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=failed"));
        var row = failed.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == k.Boss.Email);
        Assert.DoesNotContain("SIR-123", row.ToString());
        Assert.Equal("InvalidOperationException", row.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task Two_workers_never_send_the_same_mail_twice()
    {
        var k = await SetupAsync();
        for (var i = 0; i < 8; i++) await Submit(k);
        factory.Mail.DelayMs = 15;
        try { await Task.WhenAll(factory.RunEmailAsync(), factory.RunEmailAsync(), factory.RunEmailAsync()); }
        finally { factory.Mail.DelayMs = 0; }
        var mails = factory.Mail.To(k.Boss.Email);
        Assert.Equal(8, mails.Count);
        Assert.Equal(8, mails.Select(m => m.Subject).Distinct().Count());
    }

    // ------------------------------------------------------------------ güvenlik

    [Fact]
    public async Task Names_with_line_breaks_cannot_inject_headers()
    {
        var k = await SetupAsync(bossName: "Ali\r\nBcc: kurban@evil.com");
        await Submit(k);
        var mail = Assert.Single(await Delivered(k.Boss));
        Assert.DoesNotContain('\r', mail.ToName);
        Assert.DoesNotContain('\n', mail.ToName);
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
    }

    [Fact]
    public async Task Demo_data_never_sends_mail_to_anyone()
    {
        var admin = await Api.RegisterAsync(factory);
        Assert.True((await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/demo-data")).Is(HttpStatusCode.Created));
        await factory.RunEmailAsync();
        Assert.DoesNotContain(factory.Mail.Sent, m => m.ToAddress.EndsWith("@demo.arcvora.example"));
        Assert.Empty(factory.Mail.To(admin.Email));                                    // master admin'e düşen demo görevleri de mail üretmez
    }

    // --------------------------------------------------------------------- test maili

    [Fact]
    public async Task The_test_mail_goes_to_the_admins_own_address_and_reports_failures_without_secrets()
    {
        var k = await SetupAsync();
        Assert.True((await k.Boss.PostAsync(Outbox(k.Admin, "/test"))).Is(HttpStatusCode.Forbidden));
        var ok = await k.Admin.PostAsync(Outbox(k.Admin, "/test"));
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        Assert.Equal(k.Admin.Email, ok.Str("to"));
        Assert.Contains("Test", Assert.Single(factory.Mail.To(k.Admin.Email)).Subject);

        factory.Mail.FailNext(new EmailSendException("SMTP kimlik doğrulaması başarısız (kullanıcı adı/parola).", false));
        var bad = await k.Admin.PostAsync(Outbox(k.Admin, "/test"));
        Assert.True(bad.Is(HttpStatusCode.BadGateway), bad.ToString());
        Assert.Equal("email_failed", bad.Str("code"));
        Assert.Contains("kimlik doğrulaması", bad.Str("title"));
    }

    [Fact]
    public async Task Outbox_is_listed_only_for_admins_and_only_their_workspace()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        await Submit(k);
        Assert.True((await k.Emp.GetAsync(Outbox(k.Admin, "/outbox"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.GetAsync(Outbox(k.Admin, "/outbox"))).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Admin.GetAsync(Outbox(k.Admin, "/outbox?status=nonsense"))).Is(HttpStatusCode.UnprocessableEntity));
        var mine = await outsider.GetAsync(Outbox(outsider, "/outbox"));
        Assert.DoesNotContain(k.Boss.Email, mine.Body.GetRawText());
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Outbox(k.Admin, "/outbox"))).StatusCode);
    }

    // ---------------------------------------------------------------- hatırlatma

    [Fact]
    public async Task Stale_tasks_get_reminders_spaced_out_and_capped()
    {
        var k = await SetupAsync();
        var req = await Submit(k);
        await factory.RunEmailAsync();
        Assert.Equal(0, await RemindersAsync());                                       // yeni görev: henüz erken

        await BackdateTasksAsync(req, 25);
        Assert.Equal(1, await RemindersAsync());
        var first = (await Delivered(k.Boss)).Where(m => m.Subject.Contains("Hatırlatma")).ToList();
        Assert.Single(first);
        Assert.Contains("gündür", first[0].Text);
        Assert.Equal(0, await RemindersAsync());                                       // hemen tekrar yok

        for (var round = 2; round <= 3; round++)
        {
            await BackdateTasksAsync(req, 25);
            Assert.Equal(1, await RemindersAsync());
        }
        await factory.RunEmailAsync();
        Assert.Equal(3, factory.Mail.To(k.Boss.Email).Count(m => m.Subject.Contains("Hatırlatma")));
        await BackdateTasksAsync(req, 25);
        Assert.Equal(0, await RemindersAsync());                                       // en fazla 3 hatırlatma
    }

    [Fact]
    public async Task Reminders_skip_decided_tasks_closed_requests_demo_apps_and_switched_off_workspaces()
    {
        var k = await SetupAsync();
        var approved = await Submit(k);
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(approved)}/approve");
        var cancelled = await Submit(k);
        await k.Emp.PostAsync($"/api/requests/{cancelled}/cancel");
        await BackdateTasksAsync(approved, 30);
        await BackdateTasksAsync(cancelled, 30);
        Assert.Equal(0, await RemindersAsync());

        var open = await Submit(k);
        await BackdateTasksAsync(open, 30);
        await k.Admin.PutAsync(Outbox(k.Admin), new { remindersEnabled = false });
        Assert.Equal(0, await RemindersAsync());
        await k.Admin.PutAsync(Outbox(k.Admin), new { remindersEnabled = true });
        Assert.Equal(1, await RemindersAsync());
    }

    [Fact]
    public async Task Concurrent_maintenance_runs_never_double_remind()
    {
        var k = await SetupAsync();
        var req = await Submit(k);
        await BackdateTasksAsync(req, 25);
        var m = factory.Services.GetRequiredService<MaintenanceService>();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => m.RunOnceAsync(CancellationToken.None)));
        Assert.Equal(1, results.Sum(r => r.RemindersQueued));
    }
}
