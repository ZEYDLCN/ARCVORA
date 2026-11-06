using System.Net;
using System.Text.Json;

namespace Arcvora.Integration.Tests;

/// <summary>G3: vekâlet — oluşturma kuralları, görevlerin devri, onay kaydı, iptal, geçişsizlik ve yeniden atama.</summary>
public class DelegationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Api Deputy, Guid App);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));      // sunucunun varsayılan UTC+3 günü
    private static string Day(int offset) => Today.AddDays(offset).ToString("yyyy-MM-dd");

    private async Task<Kit> SetupAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Müdür Mehmet");
        var deputy = await admin.NewUserAsync(factory, "app_user", displayName: "Vekil Veli");
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin", emp.UserId, boss.UserId, deputy.UserId);
        return new Kit(admin, boss, emp, deputy, app);
    }

    private static string Url(Api a, string rest = "") => $"/api/workspaces/{a.WorkspaceId}/delegations{rest}";

    private static Task<Resp> Delegate(Api from, Api to, int start = 0, int end = 7, string? reason = null) =>
        from.PostAsync(Url(from), new { toUserId = to.UserId, startsOn = Day(start), endsOn = Day(end), reason });

    private static async Task<Guid> Submit(Kit k) => (await k.Emp.SubmitAsync(k.App, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();

    private static bool HasTask(Resp list, Guid request) => list.Body.EnumerateArray().Any(t => t.GetProperty("requestId").GetGuid() == request);

    private static async Task<JsonElement> TaskFor(Api who, Guid request) =>
        (await who.GetAsync("/api/my/tasks")).Body.EnumerateArray().First(t => t.GetProperty("requestId").GetGuid() == request);

    // ------------------------------------------------------------------ oluşturma

    [Fact]
    public async Task A_user_delegates_to_a_colleague_for_a_period_and_both_see_it()
    {
        var k = await SetupAsync();
        var r = await Delegate(k.Boss, k.Deputy, 0, 7, "Yıllık izin");
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal(k.Boss.UserId.ToString(), r.Str("fromUserId"));
        Assert.Equal("Müdür Mehmet", r.Str("fromName"));
        Assert.Equal(k.Deputy.UserId.ToString(), r.Str("toUserId"));
        Assert.Equal("Vekil Veli", r.Str("toName"));
        Assert.Equal(Day(0), r.Str("startsOn"));
        Assert.Equal(Day(7), r.Str("endsOn"));
        Assert.Equal("Yıllık izin", r.Str("reason"));
        Assert.Equal("active", r.Str("status"));

        foreach (var who in new[] { k.Boss, k.Deputy })
        {
            var mine = await who.GetAsync(Url(who, "?scope=mine"));
            Assert.Equal(1, mine.Body.GetArrayLength());
        }
        Assert.Equal(0, (await k.Emp.GetAsync(Url(k.Emp, "?scope=mine"))).Body.GetArrayLength());
    }

    [Fact]
    public async Task Future_delegations_are_upcoming_and_not_effective_yet()
    {
        var k = await SetupAsync();
        var r = await Delegate(k.Boss, k.Deputy, 3, 9);
        Assert.Equal("upcoming", r.Str("status"));
        var req = await Submit(k);
        Assert.False(HasTask(await k.Deputy.GetAsync("/api/my/tasks"), req));
        var task = await k.Boss.MyPendingTaskAsync(req);
        Assert.True((await k.Deputy.PostAsync($"/api/tasks/{task}/approve")).Is(HttpStatusCode.NotFound));
    }

    [Theory]
    [InlineData(-1, 3, "start_in_past")]
    [InlineData(5, 4, "end_before_start")]
    [InlineData(0, 365, "period_too_long")]
    [InlineData(400, 401, "start_too_far")]
    public async Task Invalid_periods_are_refused_with_a_clear_reason(int start, int end, string code)
    {
        var k = await SetupAsync();
        var r = await Delegate(k.Boss, k.Deputy, start, end);
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Contains(r["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == code);
    }

    [Fact]
    public async Task Bad_people_and_malformed_input_are_refused()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);

        var self = await k.Boss.PostAsync(Url(k.Boss), new { toUserId = k.Boss.UserId, startsOn = Day(0), endsOn = Day(1) });
        Assert.Equal("invalid_delegate", self.Str("code"));
        Assert.True(self.Is(HttpStatusCode.UnprocessableEntity));
        var stranger = await k.Boss.PostAsync(Url(k.Boss), new { toUserId = outsider.UserId, startsOn = Day(0), endsOn = Day(1) });
        Assert.Equal("invalid_delegate", stranger.Str("code"));
        var ghost = await k.Boss.PostAsync(Url(k.Boss), new { toUserId = Guid.NewGuid(), startsOn = Day(0), endsOn = Day(1) });
        Assert.Equal("invalid_delegate", ghost.Str("code"));

        Assert.True((await k.Boss.PostAsync(Url(k.Boss), new { startsOn = Day(0), endsOn = Day(1) })).Is(HttpStatusCode.UnprocessableEntity));          // kişi yok
        Assert.True((await k.Boss.PostAsync(Url(k.Boss), new { toUserId = k.Deputy.UserId, endsOn = Day(1) })).Is(HttpStatusCode.UnprocessableEntity));    // başlangıç yok
        Assert.True((await k.Boss.PostAsync(Url(k.Boss), new { toUserId = k.Deputy.UserId, startsOn = "yarın", endsOn = Day(1) })).Is(HttpStatusCode.UnprocessableEntity));
        Assert.True((await k.Boss.PostAsync(Url(k.Boss), new { toUserId = k.Deputy.UserId, startsOn = "2026-02-30", endsOn = "2026-03-01" })).Is(HttpStatusCode.UnprocessableEntity));
        var longReason = await Delegate(k.Boss, k.Deputy, 0, 1, new string('x', 201));
        Assert.Contains(longReason["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == "reason_too_long");
    }

    [Fact]
    public async Task Overlapping_delegations_of_one_person_are_refused_but_revoked_ones_do_not_count()
    {
        var k = await SetupAsync();
        var first = await Delegate(k.Boss, k.Deputy, 0, 7);
        var overlap = await Delegate(k.Boss, k.Emp, 5, 12);
        Assert.True(overlap.Is(HttpStatusCode.Conflict), overlap.ToString());
        Assert.Equal("delegation_overlap", overlap.Str("code"));
        Assert.True((await Delegate(k.Boss, k.Emp, 8, 12)).Is(HttpStatusCode.Created));        // bitişik dönem serbest

        Assert.True((await k.Boss.DeleteAsync(Url(k.Boss, "/" + first.Str("id")))).Is(HttpStatusCode.OK));
        Assert.True((await Delegate(k.Boss, k.Deputy, 0, 7)).Is(HttpStatusCode.Created));       // iptal edilen artık engel değil
    }

    [Fact]
    public async Task Only_a_master_admin_may_delegate_on_behalf_of_someone_else()
    {
        var k = await SetupAsync();
        var forOther = await k.Emp.PostAsync(Url(k.Emp), new { fromUserId = k.Boss.UserId, toUserId = k.Deputy.UserId, startsOn = Day(0), endsOn = Day(3) });
        Assert.True(forOther.Is(HttpStatusCode.Forbidden), forOther.ToString());

        var byAdmin = await k.Admin.PostAsync(Url(k.Admin), new { fromUserId = k.Boss.UserId, toUserId = k.Deputy.UserId, startsOn = Day(0), endsOn = Day(3), reason = "İdare ayarladı" });
        Assert.True(byAdmin.Is(HttpStatusCode.Created), byAdmin.ToString());
        Assert.Equal(k.Boss.UserId.ToString(), byAdmin.Str("fromUserId"));
        // Kendi adına olduğunu belirtmek serbest.
        Assert.True((await k.Emp.PostAsync(Url(k.Emp), new { fromUserId = k.Emp.UserId, toUserId = k.Deputy.UserId, startsOn = Day(0), endsOn = Day(1) })).Is(HttpStatusCode.Created));
    }

    [Fact]
    public async Task Other_workspaces_get_404_and_anonymous_users_401()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await outsider.GetAsync(Url(k.Admin, "?scope=mine"))).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.PostAsync(Url(k.Admin), new { toUserId = k.Deputy.UserId, startsOn = Day(0), endsOn = Day(1) })).Is(HttpStatusCode.NotFound));
        var d = await Delegate(k.Boss, k.Deputy);
        Assert.True((await outsider.DeleteAsync(Url(k.Admin, "/" + d.Str("id")))).Is(HttpStatusCode.NotFound));
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url(k.Admin))).StatusCode);
    }

    [Fact]
    public async Task The_all_scope_is_for_master_admins_only_and_mine_is_the_default()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Deputy);
        Assert.True((await k.Emp.GetAsync(Url(k.Emp, "?scope=all"))).Is(HttpStatusCode.Forbidden));
        Assert.Equal(1, (await k.Admin.GetAsync(Url(k.Admin, "?scope=all"))).Body.GetArrayLength());
        Assert.Equal(0, (await k.Admin.GetAsync(Url(k.Admin))).Body.GetArrayLength());           // admin'in kendi vekâleti yok
        Assert.Equal(1, (await k.Boss.GetAsync(Url(k.Boss))).Body.GetArrayLength());
        Assert.True((await k.Boss.GetAsync(Url(k.Boss, "?scope=nonsense"))).Is(HttpStatusCode.UnprocessableEntity));
    }

    // ------------------------------------------------------------- görev devri

    [Fact]
    public async Task The_deputy_sees_the_task_marked_as_on_behalf_of_and_the_boss_still_sees_it()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Deputy);
        var req = await Submit(k);

        var viaDeputy = await TaskFor(k.Deputy, req);
        Assert.Equal(k.Boss.UserId.ToString(), viaDeputy.GetProperty("onBehalfOfId").GetString());
        Assert.Equal("Müdür Mehmet", viaDeputy.GetProperty("onBehalfOfName").GetString());
        var own = await TaskFor(k.Boss, req);
        Assert.False(own.TryGetProperty("onBehalfOfId", out _));
    }

    [Fact]
    public async Task The_deputy_decides_and_the_record_shows_both_names()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Deputy);
        var req = await Submit(k);
        var taskId = (await TaskFor(k.Deputy, req)).GetProperty("id").GetGuid();

        var done = await k.Deputy.PostAsync($"/api/tasks/{taskId}/approve", new { comment = "Müdür izinde, ben onayladım." });
        Assert.True(done.Is(HttpStatusCode.OK), done.ToString());
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());

        var task = done["tasks"].EnumerateArray().First(t => t.GetProperty("id").GetGuid() == taskId);
        Assert.Equal(k.Boss.UserId.ToString(), task.GetProperty("assigneeId").GetString());       // atanan değişmez
        Assert.Equal(k.Deputy.UserId.ToString(), task.GetProperty("decidedBy").GetString());      // kararı veren vekil
        var ev = done["events"].EnumerateArray().First(e => e.GetProperty("kind").GetString() == "approved");
        Assert.Equal(k.Deputy.UserId.ToString(), ev.GetProperty("actorId").GetString());
        Assert.Equal(k.Boss.UserId.ToString(), ev.GetProperty("detail").GetProperty("onBehalfOf").GetString());

        // Müdür artık bu görevi bekleyenlerde görmez; geçmişinde "vekil karar verdi" olarak görür.
        Assert.False(HasTask(await k.Boss.GetAsync("/api/my/tasks"), req));
        Assert.Contains((await k.Boss.GetAsync("/api/my/tasks?done=true")).Body.EnumerateArray(), t => t.GetProperty("requestId").GetGuid() == req);
        // Vekil de kendi geçmişinde görür.
        Assert.Contains((await k.Deputy.GetAsync("/api/my/tasks?done=true")).Body.EnumerateArray(), t => t.GetProperty("requestId").GetGuid() == req);
    }

    [Fact]
    public async Task The_deputy_can_reject_too_and_a_comment_is_still_required()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Deputy);
        var req = await Submit(k);
        var taskId = (await TaskFor(k.Deputy, req)).GetProperty("id").GetGuid();
        Assert.Equal("comment_required", (await k.Deputy.PostAsync($"/api/tasks/{taskId}/reject")).Str("code"));
        var done = await k.Deputy.PostAsync($"/api/tasks/{taskId}/reject", new { comment = "Tarih uygun değil" });
        Assert.Equal("rejected", done["summary"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_deputy_can_open_the_request_while_the_delegation_lasts_and_keeps_access_to_what_they_decided()
    {
        var k = await SetupAsync();
        var d = await Delegate(k.Boss, k.Deputy);
        var open = await Submit(k);
        var decided = await Submit(k);
        Assert.True((await k.Deputy.GetAsync($"/api/requests/{open}")).Is(HttpStatusCode.OK));
        await k.Deputy.PostAsync($"/api/tasks/{(await TaskFor(k.Deputy, decided)).GetProperty("id").GetGuid()}/approve");

        await k.Boss.DeleteAsync(Url(k.Boss, "/" + d.Str("id")));
        Assert.True((await k.Deputy.GetAsync($"/api/requests/{open}")).Is(HttpStatusCode.NotFound));         // vekâlet bitti
        Assert.True((await k.Deputy.GetAsync($"/api/requests/{decided}")).Is(HttpStatusCode.OK));            // kendi kararı: görmeye devam
    }

    [Fact]
    public async Task Revoking_ends_the_authority_immediately()
    {
        var k = await SetupAsync();
        var d = await Delegate(k.Boss, k.Deputy);
        var req = await Submit(k);
        var taskId = (await TaskFor(k.Deputy, req)).GetProperty("id").GetGuid();

        var revoked = await k.Boss.DeleteAsync(Url(k.Boss, "/" + d.Str("id")));
        Assert.True(revoked.Is(HttpStatusCode.OK), revoked.ToString());
        Assert.Equal("revoked", revoked.Str("status"));
        Assert.True((await k.Deputy.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.NotFound));
        Assert.False(HasTask(await k.Deputy.GetAsync("/api/my/tasks"), req));
        // Boss hâlâ karar verebilir.
        Assert.True((await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(req)}/approve")).Is(HttpStatusCode.OK));
        // İkinci iptal zararsız değil ama açık: zaten iptal.
        Assert.True((await k.Boss.DeleteAsync(Url(k.Boss, "/" + d.Str("id")))).Is(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task The_deputy_and_the_delegate_may_both_give_back_but_strangers_cannot_and_admins_can()
    {
        var k = await SetupAsync();
        var d1 = await Delegate(k.Boss, k.Deputy);
        Assert.True((await k.Emp.DeleteAsync(Url(k.Emp, "/" + d1.Str("id")))).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Deputy.DeleteAsync(Url(k.Deputy, "/" + d1.Str("id")))).Is(HttpStatusCode.OK));         // vekil "geri veriyorum" diyebilir
        var d2 = await Delegate(k.Boss, k.Deputy);
        Assert.True((await k.Admin.DeleteAsync(Url(k.Admin, "/" + d2.Str("id")))).Is(HttpStatusCode.OK));
        Assert.True((await k.Admin.DeleteAsync(Url(k.Admin, "/" + Guid.NewGuid()))).Is(HttpStatusCode.NotFound));
    }

    // ---------------------------------------------------------------- güvenlik

    [Fact]
    public async Task Delegation_is_not_transitive()
    {
        var k = await SetupAsync();
        var third = await k.Admin.NewUserAsync(factory, displayName: "Üçüncü");
        await k.Admin.GrantAsync(k.App, [k.Emp.UserId, k.Boss.UserId, k.Deputy.UserId, third.UserId]);
        await Delegate(k.Boss, k.Deputy);          // Müdür → Vekil
        await Delegate(k.Deputy, third);           // Vekil → Üçüncü
        var req = await Submit(k);
        Assert.False(HasTask(await third.GetAsync("/api/my/tasks"), req));
        var taskId = (await TaskFor(k.Deputy, req)).GetProperty("id").GetGuid();
        Assert.True((await third.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Nobody_can_approve_their_own_request_through_a_delegation()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Emp);              // Çalışan, kendi müdürünün vekili
        var req = await Submit(k);                  // Çalışan'ın talebi müdüre düşer
        var taskId = await k.Boss.MyPendingTaskAsync(req);
        Assert.False(HasTask(await k.Emp.GetAsync("/api/my/tasks"), req));        // kendi talebi vekâlet kutusunda hiç görünmez
        var r = await k.Emp.PostAsync($"/api/tasks/{taskId}/approve");
        Assert.True(r.Is(HttpStatusCode.Forbidden), r.ToString());
        Assert.Equal("self_approval_via_delegation", r.Str("code"));
        // Müdür hâlâ onaylayabilir.
        Assert.True((await k.Boss.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task One_person_cannot_decide_twice_in_the_same_step()
    {
        var k = await SetupAsync();
        var third = await k.Admin.NewUserAsync(factory);
        // Aynı adıma hem Müdür hem Vekil atanır (grup, "hepsi onaylamalı").
        var group = await k.Admin.NewGroupAsync("Onay kurulu", k.Boss.UserId, k.Deputy.UserId);
        var def = new
        {
            schema = 1, prefix = "KRL", summaryFields = new[] { "note" },
            fields = new object[] { new { key = "note", label = "Not", type = "text" } },
            steps = new object[] { new { id = "s1", name = "Kurul", mode = "all", approvers = new { type = "group", id = Guid.Parse(group.Str("id")) } } },
        };
        var app = await k.Admin.PublishedAppAsync(def, "Kurul onayı", k.Emp.UserId, third.UserId);
        await Delegate(k.Boss, k.Deputy);
        var req = (await k.Emp.SubmitAsync(app, new { note = "x" }))["summary"].GetProperty("id").GetGuid();

        var tasks = (await k.Deputy.GetAsync("/api/my/tasks")).Body.EnumerateArray().Where(t => t.GetProperty("requestId").GetGuid() == req).ToList();
        Assert.Equal(2, tasks.Count);                                  // kendi görevi + Müdür'ün görevi
        var first = await k.Deputy.PostAsync($"/api/tasks/{tasks[0].GetProperty("id").GetGuid()}/approve");
        Assert.True(first.Is(HttpStatusCode.OK), first.ToString());
        var second = await k.Deputy.PostAsync($"/api/tasks/{tasks[1].GetProperty("id").GetGuid()}/approve");
        Assert.True(second.Is(HttpStatusCode.Conflict), second.ToString());
        Assert.Equal("already_decided_in_step", second.Str("code"));
        Assert.Equal("in_review", (await k.Emp.GetAsync($"/api/requests/{req}"))["summary"].GetProperty("status").GetString());     // hâlâ bir kişinin onayı gerekli
    }

    [Fact]
    public async Task The_boss_and_the_deputy_deciding_at_the_same_time_produce_one_decision()
    {
        var k = await SetupAsync();
        await Delegate(k.Boss, k.Deputy);
        var req = await Submit(k);
        var taskId = (await TaskFor(k.Boss, req)).GetProperty("id").GetGuid();
        var results = await Task.WhenAll(k.Boss.PostAsync($"/api/tasks/{taskId}/approve"), k.Deputy.PostAsync($"/api/tasks/{taskId}/approve"));
        var summary = string.Join(" | ", results.Select(r => $"{(int)r.Status} {r.Body.ToString()[..Math.Min(160, r.Body.ToString().Length)]}"));
        Assert.True(results.Count(r => r.Is(HttpStatusCode.OK)) == 1, summary);
        Assert.True(results.Count(r => r.Is(HttpStatusCode.Conflict)) == 1, summary);
        var detail = await k.Emp.GetAsync($"/api/requests/{req}");
        Assert.Equal(1, detail["events"].EnumerateArray().Count(e => e.GetProperty("kind").GetString() == "approved"));
    }

    // -------------------------------------------------------- yeniden atama (admin)

    [Fact]
    public async Task A_master_admin_can_reassign_a_pending_task_and_it_is_recorded()
    {
        var k = await SetupAsync();
        var req = await Submit(k);
        var taskId = await k.Boss.MyPendingTaskAsync(req);
        var r = await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Deputy.UserId });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        Assert.Equal(k.Deputy.UserId.ToString(), r["tasks"][0].GetProperty("assigneeId").GetString());
        Assert.Contains("reassigned", r.EventKinds());
        Assert.False(HasTask(await k.Boss.GetAsync("/api/my/tasks"), req));
        Assert.True((await k.Deputy.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Reassignment_rules()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        var req = await Submit(k);
        var taskId = await k.Boss.MyPendingTaskAsync(req);

        Assert.True((await k.Boss.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Deputy.UserId })).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Emp.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Deputy.UserId })).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Deputy.UserId })).Is(HttpStatusCode.NotFound));
        Assert.Equal("invalid_assignee", (await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = outsider.UserId })).Str("code"));        // üye değil
        Assert.Equal("invalid_assignee", (await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Emp.UserId })).Str("code"));          // talep eden kendi talebini onaylayamaz
        Assert.Equal("invalid_assignee", (await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Boss.UserId })).Str("code"));         // zaten onda
        Assert.True((await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { })).Is(HttpStatusCode.UnprocessableEntity));
        Assert.True((await k.Admin.PostAsync($"/api/tasks/{Guid.NewGuid()}/reassign", new { toUserId = k.Deputy.UserId })).Is(HttpStatusCode.NotFound));

        await k.Boss.PostAsync($"/api/tasks/{taskId}/approve");
        var closed = await k.Admin.PostAsync($"/api/tasks/{taskId}/reassign", new { toUserId = k.Deputy.UserId });
        Assert.True(closed.Is(HttpStatusCode.Conflict), closed.ToString());
        Assert.Equal("task_not_pending", closed.Str("code"));
    }

    // ---------------------------------------------------------- diğer kayıtlar

    [Fact]
    public async Task Removing_a_member_revokes_their_delegations()
    {
        var k = await SetupAsync();
        var d = await Delegate(k.Boss, k.Deputy);
        Assert.True((await k.Admin.DeleteAsync($"/api/workspaces/{k.Admin.WorkspaceId}/members/{k.Deputy.UserId}")).Is(HttpStatusCode.NoContent));
        var all = await k.Admin.GetAsync(Url(k.Admin, "?scope=all"));
        Assert.Equal("revoked", all.Body.EnumerateArray().First(x => x.GetProperty("id").GetString() == d.Str("id")).GetProperty("status").GetString());
        // Yeni görev kimsenin vekil olmadığı müdürde kalır.
        var req = await Submit(k);
        Assert.True((await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(req)}/approve")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task The_printed_form_and_the_accounting_payload_show_who_acted_for_whom()
    {
        var k = await SetupAsync();
        await k.Admin.PutAsync($"/api/workspaces/{k.Admin.WorkspaceId}/accounting/settings", new { });
        var expense = await k.Admin.PublishedAppAsync(AppsKit.ExpenseDef(), "Masraf", k.Emp.UserId);
        await Delegate(k.Boss, k.Deputy);
        var req = (await k.Emp.SubmitAsync(expense, new { amount = 100m }))["summary"].GetProperty("id").GetGuid();
        var taskId = (await TaskFor(k.Deputy, req)).GetProperty("id").GetGuid();
        Assert.True((await k.Deputy.PostAsync($"/api/tasks/{taskId}/approve", new { comment = "Tamam" })).Is(HttpStatusCode.OK));

        var entries = await k.Admin.GetAsync($"/api/workspaces/{k.Admin.WorkspaceId}/accounting/entries");
        var approval = entries.Body[0].GetProperty("payload").GetProperty("approvals")[0];
        Assert.Equal("Vekil Veli", approval.GetProperty("by").GetProperty("name").GetString());
        Assert.Equal("Müdür Mehmet", approval.GetProperty("onBehalfOf").GetProperty("name").GetString());

        var pdf = await k.Emp.DownloadAsync($"/api/requests/{req}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.Status);
        var path = Path.Combine(Path.GetTempPath(), $"arcvora-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, pdf.Bytes);
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("pdftotext", $"-layout \"{path}\" -") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var text = await p.StandardOutput.ReadToEndAsync();
            Assert.Contains("Vekil Veli", text);
            Assert.Contains("Müdür Mehmet", text);
            Assert.Contains("vekâleten", text);
        }
        catch (System.ComponentModel.Win32Exception) { /* pdftotext yok */ }
        finally { File.Delete(path); }
    }
}
