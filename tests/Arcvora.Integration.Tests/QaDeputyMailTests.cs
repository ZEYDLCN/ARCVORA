using System.Net;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>
/// QA: vekâlet + e-posta birlikte. Müdür izinliyken görev onun adına gelir; kararı verebilecek kişi (vekil) görevden e-postayla da haberdar
/// edilmeli (aksi halde vekâletin amacı — "talep beklemesin" — kaybolur). Vekil görevi onay kutusunda görür ama kimse ona haber vermez.
/// </summary>
public class QaDeputyMailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Api Deputy, Guid App);

    private async Task<Kit> SetupAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür Mehmet");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan Can", managerId: boss.UserId);
        var deputy = await admin.NewUserAsync(factory, displayName: "Vekil Veli");
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin Talebi", emp.UserId);
        return new Kit(admin, boss, emp, deputy, app);
    }

    private static bool IsReminder(OutgoingMail m) => m.Subject.Contains("atırlatma");        // "Hatırlatma" ve "Vekâleten hatırlatma"

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));
    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd");

    private async Task<Resp> DelegateAsync(Kit k, int startOffset = 0, int endOffset = 5, Api? to = null) =>
        await k.Boss.PostAsync($"/api/workspaces/{k.Admin.WorkspaceId}/delegations", new { toUserId = (to ?? k.Deputy).UserId, startsOn = Day(Today.AddDays(startOffset)), endsOn = Day(Today.AddDays(endOffset)) });

    private async Task<Guid> SubmitAsync(Kit k, Api? who = null) => (await (who ?? k.Emp).SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();

    private async Task<IReadOnlyList<OutgoingMail>> Mails(Api who)
    {
        await factory.RunEmailAsync();
        return factory.Mail.To(who.Email);
    }

    private async Task BackdateAsync(Guid requestId, int hours)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ApprovalTasks" SET "CreatedAt" = now() - make_interval(hours => {hours}), "RemindedAt" = CASE WHEN "RemindedAt" IS NULL THEN NULL ELSE now() - make_interval(hours => {hours}) END WHERE "RequestId" = {requestId}""");
    }

    private async Task<int> RemindersAsync() =>
        (await factory.Services.GetRequiredService<MaintenanceService>().RunOnceAsync(CancellationToken.None)).RemindersQueued;

    [Fact]
    public async Task While_a_delegation_is_active_the_deputy_is_mailed_about_new_tasks_too_marked_as_on_behalf_of()
    {
        var k = await SetupAsync();
        Assert.True((await DelegateAsync(k)).Is(HttpStatusCode.Created));
        await SubmitAsync(k);

        var boss = (await Mails(k.Boss)).Where(m => m.Subject.Contains("Onayınız bekleniyor")).ToList();
        Assert.Single(boss);
        var deputy = (await Mails(k.Deputy)).Where(m => m.Subject.Contains("bekleniyor")).ToList();
        var mail = Assert.Single(deputy);
        Assert.Contains("Vekâleten", mail.Subject);
        Assert.Contains("Müdür Mehmet", mail.Text);                      // kimin adına
        Assert.Contains("Çalışan Can", mail.Text);                       // kimin talebi
        Assert.DoesNotContain("pass", mail.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_delegation_that_has_not_started_or_was_revoked_mails_nobody_extra()
    {
        var k = await SetupAsync();
        var future = await DelegateAsync(k, startOffset: 3, endOffset: 6);
        Assert.True(future.Is(HttpStatusCode.Created));
        await SubmitAsync(k);
        Assert.DoesNotContain(await Mails(k.Deputy), m => m.Subject.Contains("bekleniyor"));

        var k2 = await SetupAsync();
        var d = await DelegateAsync(k2);
        await k2.Boss.DeleteAsync($"/api/workspaces/{k2.Admin.WorkspaceId}/delegations/{d.Str("id")}");
        await SubmitAsync(k2);
        Assert.DoesNotContain(await Mails(k2.Deputy), m => m.Subject.Contains("bekleniyor"));
    }

    [Fact]
    public async Task A_requester_who_is_also_the_deputy_is_not_mailed_about_their_own_request()
    {
        var k = await SetupAsync();
        Assert.True((await DelegateAsync(k, to: k.Emp)).Is(HttpStatusCode.Created));         // çalışan, müdürün vekili
        await SubmitAsync(k);
        Assert.DoesNotContain(await Mails(k.Emp), m => m.Subject.Contains("bekleniyor"));
    }

    [Fact]
    public async Task A_deputy_who_turned_mail_off_is_not_mailed()
    {
        var k = await SetupAsync();
        await k.Deputy.PutAsync("/api/me/notifications", new { emailEnabled = false });
        Assert.True((await DelegateAsync(k)).Is(HttpStatusCode.Created));
        await SubmitAsync(k);
        Assert.DoesNotContain(await Mails(k.Deputy), m => m.Subject.Contains("bekleniyor"));
    }

    [Fact]
    public async Task Reminders_also_reach_the_active_deputy_and_stay_capped()
    {
        var k = await SetupAsync();
        var req = await SubmitAsync(k);                          // vekâlet sonradan başlar: görev zaten bekliyor
        Assert.True((await DelegateAsync(k)).Is(HttpStatusCode.Created));
        await factory.RunEmailAsync();
        await BackdateAsync(req, 25);
        Assert.True(await RemindersAsync() >= 1);

        Assert.Single(await Mails(k.Boss), m => IsReminder(m));
        var deputy = Assert.Single(await Mails(k.Deputy), m => IsReminder(m));
        Assert.Contains("Vekâleten", deputy.Subject);

        for (var round = 2; round <= 4; round++) { await BackdateAsync(req, 25); await RemindersAsync(); }
        await factory.RunEmailAsync();
        Assert.Equal(3, factory.Mail.To(k.Deputy.Email).Count(m => IsReminder(m)));       // en çok 3
        Assert.Equal(3, factory.Mail.To(k.Boss.Email).Count(m => IsReminder(m)));
    }

    [Fact]
    public async Task A_decided_task_sends_no_more_reminders_to_the_deputy()
    {
        var k = await SetupAsync();
        var req = await SubmitAsync(k);
        Assert.True((await DelegateAsync(k)).Is(HttpStatusCode.Created));
        var task = (await k.Deputy.GetAsync("/api/my/tasks")).Body.EnumerateArray().First(t => t.GetProperty("requestId").GetGuid() == req).GetProperty("id").GetGuid();   // vekil kararı verir
        Assert.True((await k.Deputy.PostAsync($"/api/tasks/{task}/approve")).Is(HttpStatusCode.OK));
        await BackdateAsync(req, 25);
        await RemindersAsync();
        Assert.DoesNotContain(await Mails(k.Deputy), m => IsReminder(m));
        Assert.DoesNotContain(await Mails(k.Boss), m => IsReminder(m));
    }
}
