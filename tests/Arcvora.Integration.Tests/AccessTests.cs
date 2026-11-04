using System.Net;

namespace Arcvora.Integration.Tests;

public class AccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Other_workspace_cannot_see_or_touch_a_project_by_id()
    {
        var owner = await Api.RegisterAsync(factory);
        var intruder = await Api.RegisterAsync(factory);
        var pid = await owner.CreateSampleAsync();
        var proposalId = await owner.PendingProposalAsync(pid);

        // REQ-001 kabul ölçütü: başka kiracıya ait kayda doğrudan ID ile erişim reddedilir.
        var attempts = new List<Resp>
        {
            await intruder.GetAsync($"/api/projects/{pid}"),
            await intruder.GetAsync($"/api/projects/{pid}/versions"),
            await intruder.GetAsync($"/api/projects/{pid}/versions/1"),
            await intruder.GetAsync($"/api/projects/{pid}/versions/1/views"),
            await intruder.GetAsync($"/api/projects/{pid}/versions/1/findings"),
            await intruder.GetAsync($"/api/projects/{pid}/proposals"),
            await intruder.GetAsync($"/api/projects/{pid}/proposals/{proposalId}"),
            await intruder.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/accept", new { }),
            await intruder.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/reject", new { }),
            await intruder.PostAsync($"/api/projects/{pid}/sources", new { text = "sızma" }),
            await intruder.PostAsync($"/api/projects/{pid}/exports", new { format = "markdown" }),
            await intruder.GetAsync($"/api/projects/{pid}/exports"),
            await intruder.GetAsync($"/api/projects/{pid}/audit"),
            await intruder.PostAsync($"/api/projects/{pid}/analyses"),
            await intruder.DeleteAsync($"/api/projects/{pid}"),
        };
        Assert.All(attempts, r => Assert.True(r.Is(HttpStatusCode.NotFound), r.ToString()));

        // Aynı kimlikleri kullansa bile saldırganın listesinde proje yoktur.
        var list = await intruder.GetAsync("/api/projects");
        Assert.Equal(0, list.Body.GetArrayLength());
        // Kurban etkilenmedi
        Assert.Equal(1, await owner.HeadAsync(pid));
    }

    [Fact]
    public async Task A_job_of_another_workspace_is_not_readable_or_cancellable()
    {
        var owner = await Api.RegisterAsync(factory);
        var intruder = await Api.RegisterAsync(factory);
        var pid = await owner.CreateProjectAsync("Çalışanlar masraf girecek. Yönetici onaylayacak.");
        var job = await owner.PostAsync($"/api/projects/{pid}/analyses");
        Assert.True(job.Is(HttpStatusCode.Accepted), job.ToString());
        var jobId = job.Str("id");

        Assert.True((await intruder.GetAsync($"/api/jobs/{jobId}")).Is(HttpStatusCode.NotFound));
        Assert.True((await intruder.PostAsync($"/api/jobs/{jobId}/cancel")).Is(HttpStatusCode.NotFound));
        Assert.True((await owner.GetAsync($"/api/jobs/{jobId}")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Cannot_create_a_project_in_a_workspace_you_are_not_a_member_of()
    {
        var owner = await Api.RegisterAsync(factory);
        var intruder = await Api.RegisterAsync(factory);
        var r = await intruder.PostAsync("/api/projects", new { workspaceId = owner.WorkspaceId, name = "x" });
        Assert.True(r.Is(HttpStatusCode.NotFound), r.ToString());
        var s = await intruder.PostAsync("/api/samples/expense-management", new { workspaceId = owner.WorkspaceId });
        Assert.True(s.Is(HttpStatusCode.NotFound), s.ToString());
    }

    [Fact]
    public async Task Roles_are_enforced_server_side()
    {
        var admin = await Api.RegisterAsync(factory);
        var reader = await Api.RegisterAsync(factory);
        var editor = await Api.RegisterAsync(factory);
        var editorNoAccept = await Api.RegisterAsync(factory);
        await admin.AddMemberAsync(reader, "reader");
        await admin.AddMemberAsync(editor, "editor");
        await admin.AddMemberAsync(editorNoAccept, "editor", canAccept: false);

        var pid = await admin.CreateSampleAsync();
        var proposalId = await admin.PendingProposalAsync(pid);

        // Okuyucu görür ama değiştiremez
        Assert.True((await reader.GetAsync($"/api/projects/{pid}")).Is(HttpStatusCode.OK));
        Assert.True((await reader.PostAsync($"/api/projects/{pid}/sources", new { text = "yeni" })).Is(HttpStatusCode.Forbidden));
        Assert.True((await reader.PostAsync($"/api/projects/{pid}/analyses")).Is(HttpStatusCode.Forbidden));
        Assert.True((await reader.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/accept", new { })).Is(HttpStatusCode.Forbidden));
        Assert.True((await reader.PostAsync("/api/projects", new { workspaceId = admin.WorkspaceId, name = "x" })).Is(HttpStatusCode.Forbidden));
        Assert.True((await reader.GetAsync($"/api/projects/{pid}/audit")).Is(HttpStatusCode.Forbidden));
        Assert.True((await reader.DeleteAsync($"/api/projects/{pid}")).Is(HttpStatusCode.Forbidden));

        // Kabul yetkisi ayrı sınırlandırılabilir
        var edit = await editorNoAccept.PostAsync($"/api/projects/{pid}/sources", new { text = "Düzenleyici kaynak ekleyebilir." });
        Assert.True(edit.Is(HttpStatusCode.Created), edit.ToString());
        Assert.True((await editorNoAccept.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/accept", new { })).Is(HttpStatusCode.Forbidden));
        Assert.True((await editorNoAccept.PostAsync($"/api/projects/{pid}/proposals/{proposalId}/reject", new { })).Is(HttpStatusCode.Forbidden));

        // Düzenleyici (kabul yetkili) ve yönetici kabul edebilir; yalnızca yönetici silebilir ve denetim kaydını görür
        Assert.True((await editor.DeleteAsync($"/api/projects/{pid}")).Is(HttpStatusCode.Forbidden));
        Assert.True((await admin.GetAsync($"/api/projects/{pid}/audit")).Is(HttpStatusCode.OK));
    }

    [Fact]
    public async Task Cannot_remove_the_last_admin_or_demote_yourself_out_of_admin()
    {
        var admin = await Api.RegisterAsync(factory);
        var r = await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{admin.UserId}");
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("last_admin", r.Str("code"));
        var demote = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/members", new { email = admin.Email, role = "reader" });
        Assert.True(demote.Is(HttpStatusCode.Conflict), demote.ToString());
    }

    [Fact]
    public async Task Authentication_rules()
    {
        var anon = new Api(factory.CreateClient());
        Assert.True((await anon.GetAsync("/api/projects")).Is(HttpStatusCode.Unauthorized));

        var email = $"auth-{Guid.NewGuid():N}@example.com";
        var user = await Api.RegisterAsync(factory, email, "correct-horse-battery");

        var dup = await anon.PostAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = "correct-horse-battery" });
        Assert.True(dup.Is(HttpStatusCode.Conflict), dup.ToString());
        Assert.Equal("email_taken", dup.Str("code"));

        var weak = await anon.PostAsync("/api/auth/register", new { email = "x@example.com", password = "short" });
        Assert.True(weak.Is(HttpStatusCode.UnprocessableEntity), weak.ToString());
        var bad = await anon.PostAsync("/api/auth/register", new { email = "not-an-email", password = "correct-horse-battery" });
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());

        var wrong = await anon.PostAsync("/api/auth/login", new { email, password = "wrong-password-123" });
        var unknown = await anon.PostAsync("/api/auth/login", new { email = "nobody@example.com", password = "wrong-password-123" });
        Assert.True(wrong.Is(HttpStatusCode.Unauthorized));
        Assert.True(unknown.Is(HttpStatusCode.Unauthorized));
        Assert.Equal(wrong.Str("title"), unknown.Str("title"));         // kullanıcı varlığı sızdırılmaz

        var ok = await anon.PostAsync("/api/auth/login", new { email, password = "correct-horse-battery" });
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        Assert.False(string.IsNullOrEmpty(ok.Str("token")));
        Assert.Equal(user.UserId.ToString(), ok["user"].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Unselectable_profile_is_rejected_and_listed_as_planned()
    {
        var u = await Api.RegisterAsync(factory);
        var r = await u.PostAsync("/api/projects", new { workspaceId = u.WorkspaceId, name = "Java projesi", techProfileId = "springboot" });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("profile_not_available", r.Str("code"));

        var profiles = await u.GetAsync("/api/profiles");
        var spring = profiles.Body.EnumerateArray().First(p => p.GetProperty("id").GetString() == "springboot");
        Assert.False(spring.GetProperty("selectable").GetBoolean());
        Assert.Equal("planned", spring.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Error_responses_do_not_leak_internals_and_have_machine_readable_codes()
    {
        var u = await Api.RegisterAsync(factory);
        var r = await u.GetAsync($"/api/projects/{Guid.NewGuid()}");
        Assert.True(r.Is(HttpStatusCode.NotFound));
        Assert.Equal("not_found", r.Str("code"));
        Assert.DoesNotContain("Exception", r.Body.ToString());
        Assert.DoesNotContain("at Arcvora", r.Body.ToString());
    }
}
