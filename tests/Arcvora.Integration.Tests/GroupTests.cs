using System.Net;

namespace Arcvora.Integration.Tests;

/// <summary>Faz A: gruplar (onaylayıcı ve muhasebe grubu olarak kullanılır).</summary>
public class GroupTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Url(Api a, string rest = "") => $"/api/workspaces/{a.WorkspaceId}/groups{rest}";

    [Fact]
    public async Task Admin_creates_a_group_and_sets_its_members()
    {
        var admin = await Api.RegisterAsync(factory);
        var u1 = await admin.NewUserAsync(factory);
        var u2 = await admin.NewUserAsync(factory);

        var created = await admin.PostAsync(Url(admin), new { name = "Muhasebe" });
        Assert.True(created.Is(HttpStatusCode.Created), created.ToString());
        var id = created.Str("id");
        Assert.Equal(0, created["memberIds"].GetArrayLength());

        var set = await admin.PutAsync(Url(admin, $"/{id}/members"), new { userIds = new[] { u1.UserId, u2.UserId, u1.UserId } });
        Assert.True(set.Is(HttpStatusCode.OK), set.ToString());
        Assert.Equal(2, set["memberIds"].GetArrayLength());     // tekrarlar tek sayılır

        var list = await admin.GetAsync(Url(admin));
        Assert.Equal(1, list.Body.GetArrayLength());
        Assert.Equal("Muhasebe", list.Body[0].GetProperty("name").GetString());
        Assert.Equal(2, list.Body[0].GetProperty("memberIds").GetArrayLength());

        // Üyeleri değiştirmek eskilerini siler.
        var replaced = await admin.PutAsync(Url(admin, $"/{id}/members"), new { userIds = new[] { u2.UserId } });
        Assert.Equal(1, replaced["memberIds"].GetArrayLength());
    }

    [Fact]
    public async Task Group_names_are_unique_per_workspace_and_validated()
    {
        var admin = await Api.RegisterAsync(factory);
        Assert.True((await admin.PostAsync(Url(admin), new { name = "İK" })).Is(HttpStatusCode.Created));
        var dup = await admin.PostAsync(Url(admin), new { name = "  ik " });
        Assert.True(dup.Is(HttpStatusCode.Conflict), dup.ToString());
        Assert.Equal("group_name_taken", dup.Str("code"));
        Assert.Equal("invalid_name", (await admin.PostAsync(Url(admin), new { name = "   " })).Str("code"));
        Assert.Equal("invalid_name", (await admin.PostAsync(Url(admin), new { name = new string('x', 101) })).Str("code"));

        // Başka çalışma alanında aynı ad serbest.
        var other = await Api.RegisterAsync(factory);
        Assert.True((await other.PostAsync(Url(other), new { name = "İK" })).Is(HttpStatusCode.Created));
    }

    [Fact]
    public async Task Only_workspace_members_can_be_group_members()
    {
        var admin = await Api.RegisterAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var id = (await admin.PostAsync(Url(admin), new { name = "Yöneticiler" })).Str("id");

        var r = await admin.PutAsync(Url(admin, $"/{id}/members"), new { userIds = new[] { outsider.UserId } });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("invalid_member", r.Str("code"));
    }

    [Fact]
    public async Task Non_admins_cannot_manage_groups_and_other_workspaces_get_404()
    {
        var admin = await Api.RegisterAsync(factory);
        var editor = await admin.NewUserAsync(factory, "editor");
        var appUser = await admin.NewUserAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var id = (await admin.PostAsync(Url(admin), new { name = "Genel müdür" })).Str("id");

        foreach (var who in new[] { editor, appUser })
        {
            Assert.True((await who.GetAsync(Url(admin))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.PostAsync(Url(admin), new { name = "x" })).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.PutAsync(Url(admin, $"/{id}/members"), new { userIds = Array.Empty<Guid>() })).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.DeleteAsync(Url(admin, $"/{id}"))).Is(HttpStatusCode.Forbidden));
        }

        Assert.True((await outsider.GetAsync(Url(admin))).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.DeleteAsync(Url(admin, $"/{id}"))).Is(HttpStatusCode.NotFound));
        // Kimliği başka çalışma alanından kullanmak da 404.
        Assert.True((await outsider.PutAsync(Url(outsider, $"/{id}/members"), new { userIds = Array.Empty<Guid>() })).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Rename_and_delete_work_and_removing_a_member_drops_group_membership()
    {
        var admin = await Api.RegisterAsync(factory);
        var u = await admin.NewUserAsync(factory);
        var id = (await admin.PostAsync(Url(admin), new { name = "Eski ad" })).Str("id");
        await admin.PutAsync(Url(admin, $"/{id}/members"), new { userIds = new[] { u.UserId } });

        var renamed = await admin.PatchAsync(Url(admin, $"/{id}"), new { name = "Yeni ad" });
        Assert.True(renamed.Is(HttpStatusCode.OK), renamed.ToString());
        Assert.Equal("Yeni ad", renamed.Str("name"));

        // Çalışma alanından çıkarılan kullanıcı gruptan da düşer.
        Assert.True((await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{u.UserId}")).Is(HttpStatusCode.NoContent));
        var list = await admin.GetAsync(Url(admin));
        Assert.Equal(0, list.Body[0].GetProperty("memberIds").GetArrayLength());

        Assert.True((await admin.DeleteAsync(Url(admin, $"/{id}"))).Is(HttpStatusCode.NoContent));
        Assert.Equal(0, (await admin.GetAsync(Url(admin))).Body.GetArrayLength());
    }
}
