using System.Net;
using System.Text.RegularExpressions;
using static Arcvora.Integration.Tests.AppsKit;

namespace Arcvora.Integration.Tests;

/// <summary>Faz C: uygulama tasarımı, yayın/sürümler, kullanım yetkileri ve erişim kuralları.</summary>
public class AppsDesignTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string[] Codes(Resp r, string prop = "issues") => r[prop].EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToArray();

    [Fact]
    public async Task A_new_app_is_a_draft_whose_issues_list_what_is_missing()
    {
        var admin = await Api.RegisterAsync(factory);
        var created = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/apps", new { name = "Boş uygulama" });
        Assert.True(created.Is(HttpStatusCode.Created), created.ToString());
        Assert.Equal("draft", created["summary"].GetProperty("status").GetString());
        Assert.Equal(0, created["summary"].GetProperty("versionNumber").GetInt32());
        Assert.Contains("fields_required", Codes(created));
        Assert.Contains("steps_required", Codes(created));

        var valid = await admin.CreateAppAsync();
        var detail = await admin.GetAsync($"/api/apps/{valid}");
        Assert.Equal(0, detail["issues"].GetArrayLength());
        Assert.True(detail["summary"].GetProperty("hasDraftChanges").GetBoolean());     // henüz hiç yayınlanmadı
    }

    [Fact]
    public async Task Publishing_an_invalid_definition_is_refused_with_the_issue_list()
    {
        var admin = await Api.RegisterAsync(factory);
        var id = (await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/apps", new { name = "Eksik" })).Body.GetProperty("summary").GetProperty("id").GetString();
        var r = await admin.PostAsync($"/api/apps/{id}/publish");
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("invalid_definition", r.Str("code"));
        Assert.Contains("steps_required", Codes(r, "details"));
    }

    [Fact]
    public async Task Publishing_creates_immutable_numbered_versions_and_detects_no_changes()
    {
        var admin = await Api.RegisterAsync(factory);
        var id = await admin.CreateAppAsync();

        var v1 = await admin.PostAsync($"/api/apps/{id}/publish");
        Assert.True(v1.Is(HttpStatusCode.OK), v1.ToString());
        Assert.Equal("published", v1["summary"].GetProperty("status").GetString());
        Assert.Equal(1, v1["summary"].GetProperty("versionNumber").GetInt32());
        Assert.False(v1["summary"].GetProperty("hasDraftChanges").GetBoolean());

        var again = await admin.PostAsync($"/api/apps/{id}/publish");
        Assert.True(again.Is(HttpStatusCode.Conflict), again.ToString());
        Assert.Equal("nothing_to_publish", again.Str("code"));

        var edit = await admin.PutAsync($"/api/apps/{id}", new { baseRevision = v1["summary"].GetProperty("revision").GetInt32(), definition = LeaveDef(prefix: "IZ2") });
        Assert.True(edit.Is(HttpStatusCode.OK), edit.ToString());
        Assert.True(edit["summary"].GetProperty("hasDraftChanges").GetBoolean());
        // Yayınlanmış sürüm taslaktan etkilenmez.
        Assert.Equal("IZN", edit["published"].GetProperty("prefix").GetString());
        Assert.Equal("IZ2", edit["draft"].GetProperty("prefix").GetString());

        var v2 = await admin.PostAsync($"/api/apps/{id}/publish");
        Assert.Equal(2, v2["summary"].GetProperty("versionNumber").GetInt32());
        Assert.Equal("IZ2", v2["published"].GetProperty("prefix").GetString());
    }

    [Fact]
    public async Task Draft_revision_conflicts_are_detected()
    {
        var admin = await Api.RegisterAsync(factory);
        var id = await admin.CreateAppAsync();
        var first = await admin.PutAsync($"/api/apps/{id}", new { baseRevision = 1, name = "Ad 1" });
        Assert.True(first.Is(HttpStatusCode.OK), first.ToString());
        Assert.Equal(2, first["summary"].GetProperty("revision").GetInt32());

        var stale = await admin.PutAsync($"/api/apps/{id}", new { baseRevision = 1, name = "Ad 2" });
        Assert.True(stale.Is(HttpStatusCode.Conflict), stale.ToString());
        Assert.Equal("revision_conflict", stale.Str("code"));
        Assert.Equal("Ad 1", (await admin.GetAsync($"/api/apps/{id}"))["summary"].GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("reader")]
    [InlineData("app_user")]
    public async Task Only_the_master_admin_can_design_apps(string role)
    {
        var admin = await Api.RegisterAsync(factory);
        var id = await admin.CreateAppAsync();
        var other = await admin.NewUserAsync(factory, role);
        var ws = admin.WorkspaceId;

        var attempts = new List<Resp>
        {
            await other.GetAsync($"/api/workspaces/{ws}/apps"),
            await other.PostAsync($"/api/workspaces/{ws}/apps", new { name = "x" }),
            await other.GetAsync($"/api/apps/{id}"),
            await other.PutAsync($"/api/apps/{id}", new { name = "x" }),
            await other.PostAsync($"/api/apps/{id}/publish"),
            await other.PostAsync($"/api/apps/{id}/archive"),
            await other.DeleteAsync($"/api/apps/{id}"),
            await other.GetAsync($"/api/apps/{id}/grants"),
            await other.PutAsync($"/api/apps/{id}/grants", new { userIds = Array.Empty<Guid>() }),
            await other.GetAsync($"/api/workspaces/{ws}/requests"),
        };
        Assert.All(attempts, r => Assert.True(r.Is(HttpStatusCode.Forbidden), r.ToString()));
    }

    [Fact]
    public async Task Another_workspace_cannot_see_or_change_the_app()
    {
        var admin = await Api.RegisterAsync(factory);
        var id = await admin.PublishedAppAsync();
        var outsider = await Api.RegisterAsync(factory);
        var attempts = new List<Resp>
        {
            await outsider.GetAsync($"/api/workspaces/{admin.WorkspaceId}/apps"),
            await outsider.GetAsync($"/api/apps/{id}"),
            await outsider.PutAsync($"/api/apps/{id}", new { name = "x" }),
            await outsider.PostAsync($"/api/apps/{id}/publish"),
            await outsider.DeleteAsync($"/api/apps/{id}"),
            await outsider.GetAsync($"/api/apps/{id}/form"),
            await outsider.SubmitAsync(id, Leave()),
        };
        Assert.All(attempts, r => Assert.True(r.Is(HttpStatusCode.NotFound), r.ToString()));
    }

    [Fact]
    public async Task Approvers_must_exist_in_the_workspace_before_publishing()
    {
        var admin = await Api.RegisterAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var ghostGroup = Guid.NewGuid();
        var id = await admin.CreateAppAsync(LeaveDef(gmGroup: ghostGroup));
        var r = await admin.PostAsync($"/api/apps/{id}/publish");
        Assert.Equal("invalid_definition", r.Str("code"));
        Assert.Contains("approver_not_found", Codes(r, "details"));

        // Gerçek grup yayınlanabilir; başka çalışma alanının kullanıcısı onaylayıcı olamaz.
        var real = (await admin.NewGroupAsync("Genel müdür")).Id();
        await admin.PutAsync($"/api/apps/{id}", new { definition = LeaveDef(gmGroup: real) });
        Assert.True((await admin.PostAsync($"/api/apps/{id}/publish")).Is(HttpStatusCode.OK));

        var withForeignUser = new
        {
            schema = 1, prefix = "X", fields = new object[] { new { key = "a", label = "A", type = "text" } },
            steps = new object[] { new { id = "s1", name = "Kişi", approvers = new { type = "user", id = outsider.UserId } } },
        };
        var id2 = await admin.CreateAppAsync(withForeignUser, "Yabancı");
        Assert.Contains("approver_not_found", Codes(await admin.PostAsync($"/api/apps/{id2}/publish"), "details"));
    }

    [Fact]
    public async Task Archiving_hides_the_app_from_users_and_restoring_brings_it_back()
    {
        var admin = await Api.RegisterAsync(factory);
        var user = await admin.NewUserAsync(factory);
        var id = await admin.PublishedAppAsync(null, "İzin", user.UserId);
        Assert.Equal(1, (await user.GetAsync("/api/my/apps")).Body.GetArrayLength());

        var archived = await admin.PostAsync($"/api/apps/{id}/archive");
        Assert.Equal("archived", archived.Str("status"));
        Assert.Equal(0, (await user.GetAsync("/api/my/apps")).Body.GetArrayLength());
        Assert.Equal("app_not_published", (await user.SubmitAsync(id, Leave())).Str("code"));

        var restored = await admin.PostAsync($"/api/apps/{id}/restore");
        Assert.Equal("published", restored.Str("status"));
        Assert.Equal(1, (await user.GetAsync("/api/my/apps")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Only_never_published_drafts_can_be_deleted()
    {
        var admin = await Api.RegisterAsync(factory);
        var draft = await admin.CreateAppAsync();
        Assert.True((await admin.DeleteAsync($"/api/apps/{draft}")).Is(HttpStatusCode.NoContent));
        Assert.True((await admin.GetAsync($"/api/apps/{draft}")).Is(HttpStatusCode.NotFound));

        var published = await admin.PublishedAppAsync();
        var r = await admin.DeleteAsync($"/api/apps/{published}");
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("app_published", r.Str("code"));
    }

    [Fact]
    public async Task My_apps_respects_user_and_group_grants_and_hides_drafts()
    {
        var admin = await Api.RegisterAsync(factory);
        var direct = await admin.NewUserAsync(factory);
        var viaGroup = await admin.NewUserAsync(factory);
        var nobody = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Satış", viaGroup.UserId)).Id();

        var app = await admin.PublishedAppAsync(null, "İzin");
        await admin.GrantAsync(app, [direct.UserId], [group]);
        var draftOnly = await admin.CreateAppAsync(null, "Taslak");
        await admin.GrantAsync(draftOnly, [direct.UserId]);

        Assert.Equal(["İzin"], (await direct.GetAsync("/api/my/apps")).Body.EnumerateArray().Select(a => a.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(1, (await viaGroup.GetAsync("/api/my/apps")).Body.GetArrayLength());
        Assert.Equal(0, (await nobody.GetAsync("/api/my/apps")).Body.GetArrayLength());
        Assert.Equal(1, (await admin.GetAsync("/api/my/apps")).Body.GetArrayLength());      // master admin yayınlanmış her şeyi görür

        // Yetkisiz kullanıcı formu açamaz ve talep oluşturamaz.
        Assert.Equal("no_app_access", (await nobody.GetAsync($"/api/apps/{app}/form")).Str("code"));
        Assert.True((await nobody.SubmitAsync(app, Leave())).Is(HttpStatusCode.Forbidden));
        Assert.True((await direct.GetAsync($"/api/apps/{app}/form")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Grants_are_limited_to_workspace_members_and_groups()
    {
        var admin = await Api.RegisterAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var id = await admin.CreateAppAsync();

        var bad = await admin.PutAsync($"/api/apps/{id}/grants", new { userIds = new[] { outsider.UserId }, groupIds = Array.Empty<Guid>() });
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.Equal("invalid_grant", bad.Str("code"));
        Assert.Equal("invalid_grant", (await admin.PutAsync($"/api/apps/{id}/grants", new { userIds = Array.Empty<Guid>(), groupIds = new[] { Guid.NewGuid() } })).Str("code"));

        var u = await admin.NewUserAsync(factory);
        await admin.GrantAsync(id, [u.UserId, u.UserId]);
        var grants = await admin.GetAsync($"/api/apps/{id}/grants");
        Assert.Equal(1, grants["userIds"].GetArrayLength());
    }

    [Fact]
    public async Task Names_icons_and_sizes_are_validated()
    {
        var admin = await Api.RegisterAsync(factory);
        var url = $"/api/workspaces/{admin.WorkspaceId}/apps";
        Assert.Equal("invalid_name", (await admin.PostAsync(url, new { name = "  " })).Str("code"));
        Assert.Equal("invalid_name", (await admin.PostAsync(url, new { name = new string('x', 201) })).Str("code"));
        Assert.Equal("invalid_icon", (await admin.PostAsync(url, new { name = "x", icon = "rocket" })).Str("code"));
        Assert.Equal("invalid_description", (await admin.PostAsync(url, new { name = "x", description = new string('x', 1001) })).Str("code"));
    }

    [Fact]
    public async Task The_workspace_directory_lists_members_for_any_member_but_not_outsiders()
    {
        var admin = await Api.RegisterAsync(factory);
        var user = await admin.NewUserAsync(factory, displayName: "Ayşe");
        var outsider = await Api.RegisterAsync(factory);

        var list = await user.GetAsync($"/api/workspaces/{admin.WorkspaceId}/directory");
        Assert.True(list.Is(HttpStatusCode.OK), list.ToString());
        Assert.Contains("Ayşe", list.Body.EnumerateArray().Select(e => e.GetProperty("displayName").GetString()));
        Assert.True((await outsider.GetAsync($"/api/workspaces/{admin.WorkspaceId}/directory")).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Request_numbers_use_the_published_prefix()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory);
        var worker = await admin.NewUserAsync(factory, managerId: boss.UserId);
        var id = await admin.PublishedAppAsync(LeaveDef(prefix: "AVN"), "Avans", worker.UserId);
        var r = await worker.SubmitAsync(id, Leave());
        Assert.Matches(new Regex(@"^AVN-\d{4}-0001$"), r["summary"].GetProperty("number").GetString()!);
    }
}
