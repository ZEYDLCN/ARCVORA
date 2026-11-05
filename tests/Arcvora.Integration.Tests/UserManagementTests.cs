using System.Net;

namespace Arcvora.Integration.Tests;

/// <summary>Faz A: master admin kullanıcı açar; app kullanıcısı mimari projeleri göremez; parola ve yönetici ilişkisi.</summary>
public class UserManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_creates_an_app_user_who_can_log_in_with_role_app_user()
    {
        var admin = await Api.RegisterAsync(factory);
        var email = $"calisan-{Guid.NewGuid():N}@example.com";
        var created = await admin.CreateUserAsync(email, "app_user", displayName: "Ayşe Çalışan");
        Assert.True(created.Is(HttpStatusCode.Created), created.ToString());
        Assert.Equal("app_user", created.Str("role"));
        Assert.True(created["mustChangePassword"].GetBoolean());

        var user = await Api.LoginAsync(factory, email, "temporary-pass-1");
        var me = await user.GetAsync("/api/me");
        Assert.Equal("app_user", me["workspaces"][0].GetProperty("role").GetString());
        Assert.Equal(admin.WorkspaceId.ToString(), me["workspaces"][0].GetProperty("id").GetString());
        Assert.True(me["mustChangePassword"].GetBoolean());
    }

    [Fact]
    public async Task App_user_cannot_see_or_touch_architecture_projects()
    {
        var admin = await Api.RegisterAsync(factory);
        var pid = await admin.CreateSampleAsync();
        var proposalId = await admin.PendingProposalAsync(pid);
        var user = await admin.NewUserAsync(factory);

        // Aynı çalışma alanının üyesi olsa bile proje varlığı sızdırılmaz: 404.
        var attempts = new List<Resp>
        {
            await user.GetAsync($"/api/projects/{pid}"),
            await user.GetAsync($"/api/projects/{pid}/versions"),
            await user.GetAsync($"/api/projects/{pid}/versions/1/views"),
            await user.GetAsync($"/api/projects/{pid}/proposals"),
            await user.GetAsync($"/api/projects/{pid}/proposals/{proposalId}"),
            await user.GetAsync($"/api/projects/{pid}/exports"),
            await user.GetAsync($"/api/projects/{pid}/audit"),
            await user.PostAsync($"/api/projects/{pid}/sources", new { text = "sızma" }),
            await user.PostAsync($"/api/projects/{pid}/analyses"),
            await user.PostAsync($"/api/projects/{pid}/exports", new { format = "markdown" }),
            await user.DeleteAsync($"/api/projects/{pid}"),
        };
        Assert.All(attempts, r => Assert.True(r.Is(HttpStatusCode.NotFound), r.ToString()));

        var list = await user.GetAsync("/api/projects");
        Assert.True(list.Is(HttpStatusCode.OK));
        Assert.Equal(0, list.Body.GetArrayLength());

        // Oluşturma ve yönetim uçları 403.
        Assert.True((await user.PostAsync("/api/samples/expense-management", new { workspaceId = admin.WorkspaceId })).Is(HttpStatusCode.Forbidden));
        Assert.True((await user.PostAsync("/api/projects", new { workspaceId = admin.WorkspaceId, name = "x", description = "", techProfileId = "aspnetcore", ideaText = "" })).Is(HttpStatusCode.Forbidden));
        Assert.True((await user.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members")).Is(HttpStatusCode.Forbidden));
        Assert.True((await user.CreateUserAsync()).Is(HttpStatusCode.Forbidden));
    }

    [Theory]
    [InlineData("editor")]
    [InlineData("reader")]
    public async Task Only_admin_can_create_users(string role)
    {
        var admin = await Api.RegisterAsync(factory);
        var other = await admin.NewUserAsync(factory, role);
        var r = await other.CreateUserAsync();
        Assert.True(r.Is(HttpStatusCode.Forbidden), r.ToString());
    }

    [Fact]
    public async Task Outsider_cannot_create_users_in_someone_elses_workspace()
    {
        var admin = await Api.RegisterAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var r = await outsider.PostAsync($"/api/workspaces/{admin.WorkspaceId}/users",
            new { email = "x@example.com", displayName = "x", password = "temporary-pass-1", role = "app_user" });
        Assert.True(r.Is(HttpStatusCode.NotFound), r.ToString());
    }

    [Fact]
    public async Task Creating_a_user_validates_input_and_rejects_duplicates()
    {
        var admin = await Api.RegisterAsync(factory);
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        Assert.True((await admin.CreateUserAsync(email)).Is(HttpStatusCode.Created));

        var dup = await admin.CreateUserAsync(email);
        Assert.True(dup.Is(HttpStatusCode.Conflict), dup.ToString());
        Assert.Equal("email_taken", dup.Str("code"));

        Assert.Equal("invalid_email", (await admin.CreateUserAsync("not-an-email")).Str("code"));
        Assert.Equal("weak_password", (await admin.CreateUserAsync(password: "short")).Str("code"));
        Assert.Equal("invalid_role", (await admin.CreateUserAsync(role: "root")).Str("code"));
    }

    [Fact]
    public async Task User_changes_own_password_and_the_old_one_stops_working()
    {
        var admin = await Api.RegisterAsync(factory);
        var email = $"pw-{Guid.NewGuid():N}@example.com";
        Assert.True((await admin.CreateUserAsync(email)).Is(HttpStatusCode.Created));
        var user = await Api.LoginAsync(factory, email, "temporary-pass-1");

        var wrong = await user.PostAsync("/api/me/password", new { currentPassword = "yanlis-parola-123", newPassword = "brand-new-password" });
        Assert.True(wrong.Is(HttpStatusCode.UnprocessableEntity), wrong.ToString());
        Assert.Equal("wrong_password", wrong.Str("code"));

        var weak = await user.PostAsync("/api/me/password", new { currentPassword = "temporary-pass-1", newPassword = "short" });
        Assert.Equal("weak_password", weak.Str("code"));

        var ok = await user.PostAsync("/api/me/password", new { currentPassword = "temporary-pass-1", newPassword = "brand-new-password" });
        Assert.True(ok.Is(HttpStatusCode.NoContent) || ok.Is(HttpStatusCode.OK), ok.ToString());

        var anon = new Api(factory.CreateClient());
        Assert.True((await anon.PostAsync("/api/auth/login", new { email, password = "temporary-pass-1" })).Is(HttpStatusCode.Unauthorized));
        var again = await Api.LoginAsync(factory, email, "brand-new-password");
        Assert.False((await again.GetAsync("/api/me"))["mustChangePassword"].GetBoolean());
    }

    [Fact]
    public async Task Manager_assignment_is_validated_and_cycles_are_rejected()
    {
        var admin = await Api.RegisterAsync(factory);
        var a = await admin.NewUserAsync(factory);
        var b = await admin.NewUserAsync(factory);
        var outsider = await Api.RegisterAsync(factory);

        var ok = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{b.UserId}", new { managerId = a.UserId });
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        Assert.Equal(a.UserId.ToString(), ok.Str("managerId"));

        var self = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{a.UserId}", new { managerId = a.UserId });
        Assert.Equal("invalid_manager", self.Str("code"));

        var cycle = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{a.UserId}", new { managerId = b.UserId });
        Assert.Equal("invalid_manager", cycle.Str("code"));

        var foreign = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{a.UserId}", new { managerId = outsider.UserId });
        Assert.Equal("invalid_manager", foreign.Str("code"));

        // Yönetici kaldırılır (null) ve üye listesi güncel görünür.
        var clear = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{b.UserId}", new { clearManager = true });
        Assert.True(clear.Is(HttpStatusCode.OK), clear.ToString());
        var members = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members");
        var bRow = members.Body.EnumerateArray().First(m => m.GetProperty("userId").GetString() == b.UserId.ToString());
        Assert.False(bRow.TryGetProperty("managerId", out _));
    }

    [Fact]
    public async Task Last_admin_cannot_be_demoted_through_member_update()
    {
        var admin = await Api.RegisterAsync(factory);
        var r = await admin.PatchAsync($"/api/workspaces/{admin.WorkspaceId}/members/{admin.UserId}", new { role = "app_user" });
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("last_admin", r.Str("code"));
    }
}

/// <summary>Master admin, kendi açtığı hesapların parolasını sıfırlar; kendi kaydıyla açılmış hesaplara dokunamaz.</summary>
public class PasswordResetTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Url(Api a, Guid user) => $"/api/workspaces/{a.WorkspaceId}/members/{user}/reset-password";

    [Fact]
    public async Task Admin_resets_a_managed_users_password_and_the_user_must_change_it_again()
    {
        var admin = await Api.RegisterAsync(factory);
        var email = $"reset-{Guid.NewGuid():N}@example.com";
        var created = await admin.CreateUserAsync(email, "app_user", password: "first-temporary-1");
        var userId = created.Id("userId");
        var user = await Api.LoginAsync(factory, email, "first-temporary-1");
        await user.PostAsync("/api/me/password", new { currentPassword = "first-temporary-1", newPassword = "users-own-password-1" });
        Assert.False((await Api.LoginAsync(factory, email, "users-own-password-1").ContinueWith(t => t.Result.GetAsync("/api/me")).Unwrap())["mustChangePassword"].GetBoolean());

        var members = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members");
        Assert.True(members.Body.EnumerateArray().First(m => m.GetProperty("userId").GetString() == userId.ToString()).GetProperty("passwordResettable").GetBoolean());

        var reset = await admin.PostAsync(Url(admin, userId), new { password = "reset-temporary-9" });
        Assert.True(reset.Is(HttpStatusCode.OK), reset.ToString());
        Assert.True(reset["mustChangePassword"].GetBoolean());

        var anon = new Api(factory.CreateClient());
        Assert.True((await anon.PostAsync("/api/auth/login", new { email, password = "users-own-password-1" })).Is(HttpStatusCode.Unauthorized));
        var again = await Api.LoginAsync(factory, email, "reset-temporary-9");
        Assert.True((await again.GetAsync("/api/me"))["mustChangePassword"].GetBoolean());
    }

    [Fact]
    public async Task Accounts_that_registered_themselves_cannot_be_reset_even_by_a_workspace_admin()
    {
        var admin = await Api.RegisterAsync(factory);
        var independent = await Api.RegisterAsync(factory);          // kendi çalışma alanı olan bağımsız hesap
        await admin.AddMemberAsync(independent, "reader");

        var r = await admin.PostAsync(Url(admin, independent.UserId), new { password = "attacker-chosen-1" });
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("not_managed", r.Str("code"));
        // Parolası değişmedi: sahibi hâlâ girebilir.
        Assert.True((await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email = independent.Email, password = "correct-horse-battery" })).Is(HttpStatusCode.OK));

        var list = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members");
        Assert.False(list.Body.EnumerateArray().First(m => m.GetProperty("userId").GetString() == independent.UserId.ToString()).GetProperty("passwordResettable").GetBoolean());
        // Master admin kendi parolasını sıfırlayamaz (kendi kaydıyla açıldı); /me/password kullanır.
        Assert.Equal("not_managed", (await admin.PostAsync(Url(admin, admin.UserId), new { password = "something-new-123" })).Str("code"));
    }

    [Fact]
    public async Task A_user_managed_by_one_workspace_cannot_be_reset_from_another()
    {
        var adminA = await Api.RegisterAsync(factory);
        var adminB = await Api.RegisterAsync(factory);
        var created = await adminA.CreateUserAsync(role: "app_user");
        var userId = created.Id("userId");
        await adminB.AddMemberAsync(await Api.LoginAsync(factory, created.Str("email"), "temporary-pass-1"), "reader");

        var r = await adminB.PostAsync(Url(adminB, userId), new { password = "stolen-account-123" });
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("not_managed", r.Str("code"));
    }

    [Fact]
    public async Task Only_admins_can_reset_and_input_is_validated()
    {
        var admin = await Api.RegisterAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var editor = await admin.NewUserAsync(factory, "editor");
        var target = (await admin.CreateUserAsync()).Id("userId");

        Assert.True((await editor.PostAsync(Url(admin, target), new { password = "whatever-1234" })).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.PostAsync(Url(admin, target), new { password = "whatever-1234" })).Is(HttpStatusCode.NotFound));
        Assert.True((await admin.PostAsync(Url(admin, Guid.NewGuid()), new { password = "whatever-1234" })).Is(HttpStatusCode.NotFound));
        Assert.Equal("weak_password", (await admin.PostAsync(Url(admin, target), new { password = "short" })).Str("code"));
        Assert.Equal("weak_password", (await admin.PostAsync(Url(admin, target), new { })).Str("code"));
    }
}
