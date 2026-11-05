using System.Net;
using System.Text.RegularExpressions;
using static Arcvora.Integration.Tests.AppsKit;

namespace Arcvora.Integration.Tests;

/// <summary>Faz C: talep açma, onay/ret, iptal, görünürlük, sürüm sürekliliği ve eşzamanlılık.</summary>
public class AppsRuntimeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Master admin + yönetici + çalışan (yöneticisi bellidir) + yayınlanmış, çalışana yetkili izin uygulaması.</summary>
    private async Task<(Api Admin, Api Boss, Api Worker, Guid App)> SetupAsync(object? def = null)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Yönetici");
        var worker = await admin.NewUserAsync(factory, displayName: "Çalışan", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(def, "İzin Talebi", worker.UserId);
        return (admin, boss, worker, app);
    }

    private static string[] Codes(Resp r) => r["details"].EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();

    // ---------------------------------------------------------------- talep açma

    [Fact]
    public async Task A_granted_user_submits_a_request_that_waits_for_their_manager()
    {
        var (_, boss, worker, app) = await SetupAsync();
        var r = await worker.SubmitAsync(app, Leave());
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());

        var s = r["summary"];
        Assert.Equal("in_review", s.GetProperty("status").GetString());
        Assert.Matches(new Regex(@"^IZN-\d{4}-0001$"), s.GetProperty("number").GetString()!);
        Assert.Equal("10.11.2026 · 14.11.2026 · Yıllık", s.GetProperty("summary").GetString());
        Assert.Equal("Yönetici onayı", s.GetProperty("currentStepName").GetString());
        Assert.Equal(5, r["data"].GetProperty("days").GetInt32());                       // sunucu hesapladı

        Assert.Equal(1, r["tasks"].GetArrayLength());
        Assert.Equal(boss.UserId.ToString(), r["tasks"][0].GetProperty("assigneeId").GetString());
        Assert.Equal(["submitted", "step_started"], r.EventKinds());
        Assert.True(r["canCancel"].GetBoolean());
        Assert.False(r.Body.TryGetProperty("myPendingTaskId", out _));      // talep eden kendi görevine sahip değil
    }

    [Fact]
    public async Task An_invalid_form_is_rejected_with_every_issue_and_nothing_is_created()
    {
        var (_, _, worker, app) = await SetupAsync();
        var r = await worker.SubmitAsync(app, new { end = "2026-11-01", kind = "Hediye", hack = 1 });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("invalid_request", r.Str("code"));
        Assert.Contains("required", Codes(r));
        Assert.Contains("invalid_option", Codes(r));
        Assert.Contains("unknown_field", Codes(r));
        Assert.Equal(0, (await worker.GetAsync("/api/my/requests")).Body.GetArrayLength());

        var notBefore = await worker.SubmitAsync(app, Leave("2026-11-12", "2026-11-10"));
        Assert.Contains("not_before", Codes(notBefore));
    }

    [Fact]
    public async Task A_user_field_must_name_a_workspace_member()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        var bad = await worker.SubmitAsync(app, Leave(proxy: outsider.UserId));
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.Contains("invalid_user", Codes(bad));

        Assert.True((await worker.SubmitAsync(app, Leave(proxy: boss.UserId))).Is(HttpStatusCode.Created));
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task Numbers_increase_per_prefix_across_apps_and_are_unique()
    {
        var (admin, _, worker, app) = await SetupAsync();
        var second = await admin.PublishedAppAsync(LeaveDef(), "İzin 2", worker.UserId);     // aynı önek
        var numbers = new[]
        {
            (await worker.SubmitAsync(app, Leave())).Body.GetProperty("summary").GetProperty("number").GetString()!,
            (await worker.SubmitAsync(second, Leave())).Body.GetProperty("summary").GetProperty("number").GetString()!,
            (await worker.SubmitAsync(app, Leave())).Body.GetProperty("summary").GetProperty("number").GetString()!,
        };
        Assert.Matches(@"-0001$", numbers[0]);
        Assert.Matches(@"-0002$", numbers[1]);
        Assert.Matches(@"-0003$", numbers[2]);
    }

    [Fact]
    public async Task The_same_idempotency_key_returns_the_first_request_instead_of_creating_another()
    {
        var (_, _, worker, app) = await SetupAsync();
        var first = await worker.SubmitAsync(app, Leave(), "form-123");
        var second = await worker.SubmitAsync(app, Leave(), "form-123");
        Assert.True(first.Is(HttpStatusCode.Created));
        Assert.True(second.Is(HttpStatusCode.OK), second.ToString());
        Assert.Equal(first["summary"].GetProperty("id").GetString(), second["summary"].GetProperty("id").GetString());
        Assert.Equal(1, (await worker.GetAsync("/api/my/requests")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Parallel_submissions_with_one_key_create_a_single_request()
    {
        var (_, _, worker, app) = await SetupAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => worker.SubmitAsync(app, Leave(), "burst-1")));
        Assert.All(results, r => Assert.True(r.Is(HttpStatusCode.Created) || r.Is(HttpStatusCode.OK), r.ToString()));
        Assert.Single(results.Select(r => r["summary"].GetProperty("id").GetString()).Distinct());
        Assert.Equal(1, (await worker.GetAsync("/api/my/requests")).Body.GetArrayLength());
    }

    // --------------------------------------------------------------------- onay

    [Fact]
    public async Task The_manager_approves_a_short_leave_and_the_conditional_step_is_skipped()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var gm = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Genel müdür", gm.UserId)).Id();
        var app = await admin.PublishedAppAsync(LeaveDef(gmGroup: group), "İzin", worker.UserId);

        var r = await worker.SubmitAsync(app, Leave());          // 5 gün → genel müdür adımı atlanır
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);

        var tasks = await boss.GetAsync("/api/my/tasks");
        Assert.Equal(1, tasks.Body.GetArrayLength());
        Assert.Equal("Yönetici onayı", tasks.Body[0].GetProperty("stepName").GetString());
        Assert.Equal("Çalışan", tasks.Body[0].GetProperty("requesterName").GetString());

        var taskId = await boss.MyPendingTaskAsync(requestId);
        var done = await boss.PostAsync($"/api/tasks/{taskId}/approve", new { comment = "iyi tatiller" });
        Assert.True(done.Is(HttpStatusCode.OK), done.ToString());
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
        Assert.Equal(["submitted", "step_started", "approved", "step_skipped", "completed"], done.EventKinds());
        Assert.NotNull(done["summary"].GetProperty("completedAt").GetString());

        Assert.Equal(0, (await boss.GetAsync("/api/my/tasks")).Body.GetArrayLength());
        var history = await boss.GetAsync("/api/my/tasks?done=true");
        Assert.Equal(1, history.Body.GetArrayLength());
        Assert.Equal("iyi tatiller", history.Body[0].GetProperty("comment").GetString());
    }

    [Fact]
    public async Task A_long_leave_continues_to_the_group_step_and_any_member_can_finish_it()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var gm1 = await admin.NewUserAsync(factory);
        var gm2 = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Genel müdür", gm1.UserId, gm2.UserId)).Id();
        var app = await admin.PublishedAppAsync(LeaveDef(gmGroup: group), "İzin", worker.UserId);

        var r = await worker.SubmitAsync(app, Leave("2026-11-10", "2026-11-21"));   // 12 gün
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var afterBoss = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(requestId)}/approve");
        Assert.Equal("in_review", afterBoss["summary"].GetProperty("status").GetString());
        Assert.Equal("Genel müdür", afterBoss["summary"].GetProperty("currentStepName").GetString());
        Assert.Equal(2, afterBoss["tasks"].EnumerateArray().Count(t => t.GetProperty("stepId").GetString() == "s2"));

        var final = await gm2.PostAsync($"/api/tasks/{await gm2.MyPendingTaskAsync(requestId)}/approve", new { comment = "tamam" });
        Assert.Equal("approved", final["summary"].GetProperty("status").GetString());
        // Diğer grup üyesinin görevi artık gerekmediği için atlandı.
        var gm1Task = final["tasks"].EnumerateArray().First(t => t.GetProperty("assigneeId").GetString() == gm1.UserId.ToString());
        Assert.Equal("skipped", gm1Task.GetProperty("status").GetString());
        Assert.Equal(0, (await gm1.GetAsync("/api/my/tasks")).Body.GetArrayLength());
        Assert.True((await gm1.PostAsync($"/api/tasks/{gm1Task.GetProperty("id").GetString()}/approve")).Is(HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_ends_the_request()
    {
        var (_, boss, worker, app) = await SetupAsync();
        var r = await worker.SubmitAsync(app, Leave());
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var taskId = await boss.MyPendingTaskAsync(requestId);

        var noReason = await boss.PostAsync($"/api/tasks/{taskId}/reject", new { comment = "  " });
        Assert.True(noReason.Is(HttpStatusCode.UnprocessableEntity), noReason.ToString());
        Assert.Equal("comment_required", noReason.Str("code"));
        Assert.Equal("comment_too_long", (await boss.PostAsync($"/api/tasks/{taskId}/reject", new { comment = new string('x', 1001) })).Str("code"));

        var rejected = await boss.PostAsync($"/api/tasks/{taskId}/reject", new { comment = "Yoğun dönem" });
        Assert.True(rejected.Is(HttpStatusCode.OK), rejected.ToString());
        Assert.Equal("rejected", rejected["summary"].GetProperty("status").GetString());
        Assert.Equal(["submitted", "step_started", "rejected"], rejected.EventKinds());

        // Sonuçlanmış talepte ikinci karar ve iptal reddedilir.
        Assert.Equal("request_closed", (await boss.PostAsync($"/api/tasks/{taskId}/approve")).Str("code"));
        Assert.Equal("request_closed", (await worker.PostAsync($"/api/requests/{requestId}/cancel")).Str("code"));
    }

    [Fact]
    public async Task Only_the_assignee_can_decide_a_task_and_others_get_404()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var r = await worker.SubmitAsync(app, Leave());
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var taskId = await boss.MyPendingTaskAsync(requestId);
        var stranger = await admin.NewUserAsync(factory);

        foreach (var who in new[] { worker, stranger, admin })
        {
            Assert.True((await who.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.NotFound));
            Assert.True((await who.PostAsync($"/api/tasks/{taskId}/reject", new { comment = "x" })).Is(HttpStatusCode.NotFound));
        }
        Assert.Equal("in_review", (await worker.GetAsync($"/api/requests/{requestId}"))["summary"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Two_group_members_deciding_at_once_produce_exactly_one_outcome()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var gm1 = await admin.NewUserAsync(factory);
        var gm2 = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Genel müdür", gm1.UserId, gm2.UserId)).Id();
        var app = await admin.PublishedAppAsync(LeaveDef(gmGroup: group), "İzin", worker.UserId);

        var r = await worker.SubmitAsync(app, Leave("2026-11-10", "2026-11-25"));
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(requestId)}/approve");

        var t1 = await gm1.MyPendingTaskAsync(requestId);
        var t2 = await gm2.MyPendingTaskAsync(requestId);
        var results = await Task.WhenAll(gm1.PostAsync($"/api/tasks/{t1}/approve"), gm2.PostAsync($"/api/tasks/{t2}/approve"));

        Assert.Equal(1, results.Count(x => x.Is(HttpStatusCode.OK)));
        Assert.Equal(1, results.Count(x => x.Is(HttpStatusCode.Conflict)));
        var final = await admin.GetAsync($"/api/requests/{requestId}");
        Assert.Equal("approved", final["summary"].GetProperty("status").GetString());
        Assert.Equal(2, final.EventKinds().Count(k => k == "approved"));          // yönetici + tek bir genel müdür
        Assert.Equal(1, final.EventKinds().Count(k => k == "completed"));
    }

    // -------------------------------------------------------------------- iptal

    [Fact]
    public async Task The_requester_can_cancel_while_in_review_and_pending_tasks_disappear()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var r = await worker.SubmitAsync(app, Leave());
        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var taskId = await boss.MyPendingTaskAsync(requestId);

        // Yönetici talep eden değildir: iptal edemez (404); master admin edebilir.
        Assert.True((await boss.PostAsync($"/api/requests/{requestId}/cancel")).Is(HttpStatusCode.NotFound));

        var cancelled = await worker.PostAsync($"/api/requests/{requestId}/cancel");
        Assert.True(cancelled.Is(HttpStatusCode.OK), cancelled.ToString());
        Assert.Equal("cancelled", cancelled["summary"].GetProperty("status").GetString());
        Assert.False(cancelled["canCancel"].GetBoolean());
        Assert.Equal("cancelled", cancelled.EventKinds().Last());
        Assert.Equal(0, (await boss.GetAsync("/api/my/tasks")).Body.GetArrayLength());
        Assert.Equal("request_closed", (await boss.PostAsync($"/api/tasks/{taskId}/approve")).Str("code"));

        var r2 = await worker.SubmitAsync(app, Leave());
        var adminCancel = await admin.PostAsync($"/api/requests/{r2["summary"].GetProperty("id").GetString()}/cancel");
        Assert.True(adminCancel.Is(HttpStatusCode.OK), adminCancel.ToString());
    }

    [Fact]
    public async Task An_approved_request_cannot_be_cancelled()
    {
        var (_, boss, worker, app) = await SetupAsync();
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(requestId)}/approve");
        var r = await worker.PostAsync($"/api/requests/{requestId}/cancel");
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("request_closed", r.Str("code"));
    }

    // ------------------------------------------------------------- görünürlük

    [Fact]
    public async Task Only_involved_people_and_admins_can_open_a_request_and_lists_are_scoped()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var stranger = await admin.NewUserAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);

        foreach (var who in new[] { worker, boss, admin })
            Assert.True((await who.GetAsync($"/api/requests/{requestId}")).Is(HttpStatusCode.OK));
        foreach (var who in new[] { stranger, outsider })
            Assert.True((await who.GetAsync($"/api/requests/{requestId}")).Is(HttpStatusCode.NotFound));

        Assert.Equal(1, (await worker.GetAsync("/api/my/requests")).Body.GetArrayLength());
        Assert.Equal(0, (await stranger.GetAsync("/api/my/requests")).Body.GetArrayLength());
        Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests")).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests?status=approved")).Body.GetArrayLength());
        Assert.Equal("invalid_status", (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/requests?status=bilinmeyen")).Str("code"));
        Assert.Equal(1, (await worker.GetAsync("/api/my/requests?status=in_review")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Another_workspace_cannot_touch_requests_or_tasks()
    {
        var (_, boss, worker, app) = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);
        var taskId = await boss.MyPendingTaskAsync(requestId);

        Assert.True((await outsider.PostAsync($"/api/requests/{requestId}/cancel")).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.GetAsync($"/api/workspaces/{worker.WorkspaceId}/requests")).Is(HttpStatusCode.NotFound));
    }

    // ----------------------------------------------------- onaylayıcı geri düşüşü

    [Fact]
    public async Task Without_a_manager_the_task_goes_to_the_master_admin_and_says_why()
    {
        var admin = await Api.RegisterAsync(factory);
        var worker = await admin.NewUserAsync(factory);          // yöneticisi yok
        var app = await admin.PublishedAppAsync(null, "İzin", worker.UserId);

        var r = await worker.SubmitAsync(app, Leave());
        var task = r["tasks"][0];
        Assert.Equal(admin.UserId.ToString(), task.GetProperty("assigneeId").GetString());
        Assert.Equal("manager_missing", task.GetProperty("fallback").GetString());
        var started = r["events"].EnumerateArray().First(e => e.GetProperty("kind").GetString() == "step_started");
        Assert.Equal("manager_missing", started.GetProperty("detail").GetProperty("fallback").GetString());

        var pending = await admin.GetAsync("/api/my/tasks");
        Assert.Equal("manager_missing", pending.Body[0].GetProperty("fallback").GetString());
    }

    [Fact]
    public async Task A_sole_admin_can_approve_their_own_request_but_it_is_flagged()
    {
        var admin = await Api.RegisterAsync(factory);
        var app = await admin.PublishedAppAsync(null, "İzin");
        var r = await admin.SubmitAsync(app, Leave());
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal("only_approver_is_requester", r["tasks"][0].GetProperty("fallback").GetString());

        var requestId = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var done = await admin.PostAsync($"/api/tasks/{await admin.MyPendingTaskAsync(requestId)}/approve");
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
    }

    // -------------------------------------------------- sürüm sürekliliği ve durum

    [Fact]
    public async Task In_flight_requests_keep_the_version_they_were_opened_with()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var oldRequest = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);

        // v2: yönetici onayından sonra bir de master admin adımı.
        await admin.PutAsync($"/api/apps/{app}", new { definition = LeaveDef(extraAdminStep: true) });
        var v2 = await admin.PostAsync($"/api/apps/{app}/publish");
        Assert.Equal(2, v2["summary"].GetProperty("versionNumber").GetInt32());

        var finished = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(oldRequest)}/approve");
        Assert.Equal("approved", finished["summary"].GetProperty("status").GetString());      // eski sürümde tek adım vardı
        Assert.Equal(1, finished["versionNumber"].GetInt32());

        var fresh = await worker.SubmitAsync(app, Leave());
        Assert.Equal(2, fresh["versionNumber"].GetInt32());
        var freshId = Guid.Parse(fresh["summary"].GetProperty("id").GetString()!);
        var afterBoss = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(freshId)}/approve");
        Assert.Equal("in_review", afterBoss["summary"].GetProperty("status").GetString());     // yeni sürümde master admin adımı var
        Assert.Equal("Master admin", afterBoss["summary"].GetProperty("currentStepName").GetString());
    }

    [Fact]
    public async Task An_unpublished_app_cannot_receive_requests_even_from_the_admin()
    {
        var admin = await Api.RegisterAsync(factory);
        var draft = await admin.CreateAppAsync();
        var r = await admin.SubmitAsync(draft, Leave());
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("app_not_published", r.Str("code"));
    }

    [Fact]
    public async Task A_removed_member_loses_access_to_their_requests_and_tasks()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);
        var taskId = await boss.MyPendingTaskAsync(requestId);

        Assert.True((await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{boss.UserId}")).Is(HttpStatusCode.NoContent));
        Assert.True((await boss.PostAsync($"/api/tasks/{taskId}/approve")).Is(HttpStatusCode.NotFound));
        Assert.Equal(0, (await boss.GetAsync("/api/my/tasks")).Body.GetArrayLength());
        Assert.True((await boss.GetAsync($"/api/requests/{requestId}")).Is(HttpStatusCode.NotFound));
    }
}
