using System.Net;
using static Arcvora.Integration.Tests.AppsKit;

namespace Arcvora.Integration.Tests;

/// <summary>Çalışma alanından çıkarılan onaylayıcının bekleyen görevleri takılı kalmaz: master admin'e yönlendirilir.</summary>
public class RemovedApproverTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Kinds(Resp d) => string.Join(",", d.EventKinds());

    [Fact]
    public async Task A_removed_sole_approver_hands_the_task_to_the_master_admin_and_the_request_can_finish()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Yönetici");
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(null, "İzin", worker.UserId);
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);

        Assert.True((await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{boss.UserId}")).Is(HttpStatusCode.NoContent));

        var detail = await admin.GetAsync($"/api/requests/{requestId}");
        Assert.Equal("in_review", detail["summary"].GetProperty("status").GetString());
        var tasks = detail["tasks"].EnumerateArray().ToList();
        Assert.Equal("skipped", tasks.First(t => t.GetProperty("assigneeId").GetString() == boss.UserId.ToString()).GetProperty("status").GetString());
        var replacement = tasks.Single(t => t.GetProperty("status").GetString() == "pending");
        Assert.Equal(admin.UserId.ToString(), replacement.GetProperty("assigneeId").GetString());
        Assert.Equal("assignee_removed", replacement.GetProperty("fallback").GetString());
        Assert.Contains("approver_removed", detail.EventKinds());

        var pending = await admin.GetAsync("/api/my/tasks");
        Assert.Equal("assignee_removed", pending.Body[0].GetProperty("fallback").GetString());
        var done = await admin.PostAsync($"/api/tasks/{replacement.GetProperty("id").GetString()}/approve");
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task In_any_mode_other_pending_members_keep_the_step_and_no_replacement_is_created()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var gm1 = await admin.NewUserAsync(factory);
        var gm2 = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Genel müdür", gm1.UserId, gm2.UserId)).Id();
        var app = await admin.PublishedAppAsync(LeaveDef(gmGroup: group), "İzin", worker.UserId);
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave("2026-11-01", "2026-11-20")))["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(requestId)}/approve");      // grup adımı: gm1 ve gm2 bekliyor

        await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{gm1.UserId}");
        var detail = await admin.GetAsync($"/api/requests/{requestId}");
        var pendingNow = detail["tasks"].EnumerateArray().Where(t => t.GetProperty("status").GetString() == "pending").ToList();
        Assert.Single(pendingNow);
        Assert.Equal(gm2.UserId.ToString(), pendingNow[0].GetProperty("assigneeId").GetString());
        Assert.False(pendingNow[0].TryGetProperty("fallback", out _));

        var done = await gm2.PostAsync($"/api/tasks/{pendingNow[0].GetProperty("id").GetString()}/approve");
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
    }

    [Fact]
    public async Task When_every_pending_approver_of_a_group_step_is_removed_the_admin_takes_over()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var gm = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Genel müdür", gm.UserId)).Id();
        var app = await admin.PublishedAppAsync(LeaveDef(gmGroup: group), "İzin", worker.UserId);
        var requestId = Guid.Parse((await worker.SubmitAsync(app, Leave("2026-11-01", "2026-11-20")))["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(requestId)}/approve");

        await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{gm.UserId}");
        var task = (await admin.GetAsync($"/api/requests/{requestId}"))["tasks"].EnumerateArray().Single(t => t.GetProperty("status").GetString() == "pending");
        Assert.Equal(admin.UserId.ToString(), task.GetProperty("assigneeId").GetString());
        Assert.Equal("assignee_removed", task.GetProperty("fallback").GetString());
    }

    [Fact]
    public async Task Removing_someone_without_pending_tasks_changes_nothing_and_closed_requests_are_untouched()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(null, "İzin", worker.UserId);
        var id = Guid.Parse((await worker.SubmitAsync(app, Leave()))["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id)}/approve");          // sonuçlandı
        var before = Kinds(await admin.GetAsync($"/api/requests/{id}"));

        await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{boss.UserId}");
        var after = await admin.GetAsync($"/api/requests/{id}");
        Assert.Equal(before, Kinds(after));
        Assert.Equal("approved", after["summary"].GetProperty("status").GetString());
        Assert.Equal(0, (await admin.GetAsync("/api/my/tasks")).Body.GetArrayLength());
    }
}
