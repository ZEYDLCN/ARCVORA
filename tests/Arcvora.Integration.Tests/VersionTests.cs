using System.Net;
using System.Text.Json;

namespace Arcvora.Integration.Tests;

public class VersionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static IEnumerable<string> Ids(JsonElement arr, string prop = "id") =>
        arr.EnumerateArray().Select(e => e.GetProperty(prop).GetString()!);

    [Fact]
    public async Task Accepting_the_same_proposal_twice_creates_only_one_version()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);

        var first = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        Assert.True(first.Is(HttpStatusCode.OK), first.ToString());
        Assert.False(first["alreadyAccepted"].GetBoolean());
        Assert.Equal(2, first["version"].GetProperty("number").GetInt32());

        var second = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        Assert.True(second.Is(HttpStatusCode.OK), second.ToString());
        Assert.True(second["alreadyAccepted"].GetBoolean());
        Assert.Equal(first["version"].GetProperty("id").GetString(), second["version"].GetProperty("id").GetString());

        Assert.Equal(2, await u.VersionCountAsync(pid));
        Assert.Equal(2, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Concurrent_accepts_of_the_same_proposal_yield_exactly_one_new_version()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { })));

        // Her istek ya kazanır ya da aynı sürümü idempotent olarak görür; hiçbiri 500 olmaz.
        Assert.All(results, r => Assert.True(r.Is(HttpStatusCode.OK) || r.Is(HttpStatusCode.Conflict), r.ToString()));
        Assert.Contains(results, r => r.Is(HttpStatusCode.OK));
        Assert.Equal(2, await u.VersionCountAsync(pid));
        Assert.Equal(2, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Proposal_built_on_an_old_base_cannot_overwrite_the_current_model()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);

        // Arada başka bir değişiklik: gereksinim düzenlenir -> v2
        var edit = await u.PatchAsync($"/api/projects/{pid}/requirements/REQ-004", new { baseVersion = 1, text = "Onay geçmişi yöneticiler tarafından görülebilmeli" });
        Assert.True(edit.Is(HttpStatusCode.OK), edit.ToString());
        Assert.Equal(2, edit["version"].GetProperty("number").GetInt32());

        var accept = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        Assert.True(accept.Is(HttpStatusCode.Conflict), accept.ToString());
        Assert.Equal("reassessment_required", accept.Str("code"));

        // Son yazan diğerinin değişikliğini ezmedi
        Assert.Equal(2, await u.HeadAsync(pid));
        var v2 = await u.GetAsync($"/api/projects/{pid}/versions/2");
        Assert.Contains(v2["model"].GetProperty("requirements").EnumerateArray(),
            r => r.GetProperty("text").GetString() == "Onay geçmişi yöneticiler tarafından görülebilmeli");
        var proposal = await u.GetAsync($"/api/projects/{pid}/proposals/{prop}");
        Assert.Equal("stale", proposal["summary"].GetProperty("status").GetString());
        Assert.True(proposal["summary"].GetProperty("isStale").GetBoolean());
    }

    [Fact]
    public async Task Edit_with_an_outdated_base_version_is_a_conflict()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        await u.PatchAsync($"/api/projects/{pid}/requirements/REQ-004", new { baseVersion = 1, text = "Birinci düzenleme" });

        var stale = await u.PatchAsync($"/api/projects/{pid}/requirements/REQ-004", new { baseVersion = 1, text = "Eski sürümden ikinci düzenleme" });
        Assert.True(stale.Is(HttpStatusCode.Conflict), stale.ToString());
        Assert.Equal("version_conflict", stale.Str("code"));
        Assert.Equal(2, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Client_that_reviewed_a_different_base_cannot_accept()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        var r = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { baseVersion = 7 });
        Assert.True(r.Is(HttpStatusCode.Conflict), r.ToString());
        Assert.Equal("reassessment_required", r.Str("code"));
        Assert.Equal(1, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Partial_acceptance_rejects_inconsistent_selection_and_applies_a_consistent_one()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);

        var detail = await u.GetAsync($"/api/projects/{pid}/proposals/{prop}");
        var data = detail["groups"].EnumerateArray().First(g => g.GetProperty("id").GetString() == "grp-data");
        Assert.Contains("grp-requirements", data.GetProperty("dependsOn").EnumerateArray().Select(x => x.GetString()));

        var bad = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { groupIds = new[] { "grp-data" } });
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.Equal("invalid_selection", bad.Str("code"));
        Assert.Equal(1, await u.HeadAsync(pid));
        Assert.Equal("pending", (await u.GetAsync($"/api/projects/{pid}/proposals/{prop}"))["summary"].GetProperty("status").GetString());

        var ok = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { groupIds = new[] { "grp-requirements", "grp-data" } });
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());

        var v2 = await u.GetAsync($"/api/projects/{pid}/versions/2");
        var model = v2["model"];
        Assert.Contains("ENT-007", Ids(model.GetProperty("entities")));
        Assert.DoesNotContain("API-006", Ids(model.GetProperty("endpoints")));
        Assert.DoesNotContain("FLW-003", Ids(model.GetProperty("flows")));
        Assert.Contains("kısmi kabul", v2["info"].GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Accepting_confirms_objects_but_keeps_their_system_origin_and_deferred_status()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });

        var model = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"];
        var dec8 = model.GetProperty("decisions").EnumerateArray().First(d => d.GetProperty("id").GetString() == "DEC-008");
        Assert.Equal("system_proposal", dec8.GetProperty("origin").GetString());
        Assert.Equal("confirmed", dec8.GetProperty("reviewStatus").GetString());
        var dec7 = model.GetProperty("decisions").EnumerateArray().First(d => d.GetProperty("id").GetString() == "DEC-007");
        Assert.Equal("deferred", dec7.GetProperty("reviewStatus").GetString());          // cevap bekleyen karar sessizce onaylanmaz
    }

    [Fact]
    public async Task Previous_versions_stay_immutable_and_views_follow_the_requested_version()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var before = await u.GetAsync($"/api/projects/{pid}/versions/1");
        var hash1 = before["info"].GetProperty("contentHash").GetString();

        var prop = await u.PendingProposalAsync(pid);
        await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });

        var after = await u.GetAsync($"/api/projects/{pid}/versions/1");
        Assert.Equal(hash1, after["info"].GetProperty("contentHash").GetString());
        Assert.Equal(before["model"].ToString(), after["model"].ToString());

        var v1Views = (await u.GetAsync($"/api/projects/{pid}/versions/1/views"))["views"].ToString();
        var v2Views = (await u.GetAsync($"/api/projects/{pid}/versions/2/views"))["views"].ToString();
        Assert.DoesNotContain("ENT_007", v1Views);
        Assert.Contains("ENT_007", v2Views);

        // Sürüm GUID ile de erişilebilir
        var id = before["info"].GetProperty("id").GetString();
        Assert.True((await u.GetAsync($"/api/projects/{pid}/versions/{id}")).Is(HttpStatusCode.OK));
        Assert.True((await u.GetAsync($"/api/projects/{pid}/versions/99")).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Revert_creates_a_new_version_with_the_old_content_and_keeps_history()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });

        var revert = await u.PostAsync($"/api/projects/{pid}/versions/1/revert", new { baseVersion = 2 });
        Assert.True(revert.Is(HttpStatusCode.OK), revert.ToString());
        Assert.Equal(3, revert["version"].GetProperty("number").GetInt32());

        var v1 = await u.GetAsync($"/api/projects/{pid}/versions/1");
        var v3 = await u.GetAsync($"/api/projects/{pid}/versions/3");
        Assert.Equal(v1["info"].GetProperty("contentHash").GetString(), v3["info"].GetProperty("contentHash").GetString());
        Assert.Equal(3, await u.VersionCountAsync(pid));
        Assert.Equal("revert", v3["info"].GetProperty("kind").GetString());

        var again = await u.PostAsync($"/api/projects/{pid}/versions/3/revert", new { });
        Assert.True(again.Is(HttpStatusCode.UnprocessableEntity), again.ToString());   // zaten güncel
    }

    [Fact]
    public async Task Patch_that_creates_a_dangling_reference_is_rejected_and_changes_nothing()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var r = await u.PostAsync($"/api/projects/{pid}/patches", new
        {
            baseVersion = 1,
            summary = "Kırık bağlantı",
            patch = new
            {
                operations = new object[]
                {
                    new { op = "upsert", type = "relationship", id = "REL-099", value = new { id = "REL-099", sourceId = "CMP-001", targetId = "CMP-404", communication = "sync" } },
                },
            },
        });
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("validation_failed", r.Str("code"));
        Assert.Contains("CMP-404", r.Body.ToString());
        Assert.Equal(1, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Structured_edit_adds_objects_with_provisional_ids_and_user_confirmation()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var r = await u.PostAsync($"/api/projects/{pid}/patches", new
        {
            baseVersion = 1,
            summary = "Bildirim bileşeni",
            patch = new
            {
                operations = new object[]
                {
                    new { op = "upsert", type = "component", id = "new-1", value = new { id = "new-1", name = "Bildirim servisi", kind = "module", responsibility = "E-posta" } },
                    new { op = "upsert", type = "relationship", id = "new-2", value = new { id = "new-2", sourceId = "CMP-002", targetId = "new-1", communication = "sync", description = "bildirim iste" } },
                },
            },
        });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        var model = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"];
        var cmp = model.GetProperty("components").EnumerateArray().First(c => c.GetProperty("name").GetString() == "Bildirim servisi");
        Assert.Equal("CMP-010", cmp.GetProperty("id").GetString());
        Assert.Equal("confirmed", cmp.GetProperty("reviewStatus").GetString());
        var rel = model.GetProperty("relationships").EnumerateArray().First(c => c.GetProperty("description").GetString() == "bildirim iste");
        Assert.Equal("CMP-010", rel.GetProperty("targetId").GetString());
    }

    [Fact]
    public async Task Answering_an_open_question_creates_a_version_and_requires_an_answer_text()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var empty = await u.PatchAsync($"/api/projects/{pid}/open-questions/OQ-001", new { baseVersion = 1, status = "answered" });
        Assert.True(empty.Is(HttpStatusCode.UnprocessableEntity), empty.ToString());

        var ok = await u.PatchAsync($"/api/projects/{pid}/open-questions/OQ-001", new { baseVersion = 1, answer = "Hayır, tek şirket." });
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        var q = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"].GetProperty("openQuestions").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == "OQ-001");
        Assert.Equal("answered", q.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Requirement_edit_assigns_ids_to_new_acceptance_criteria()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var r = await u.PatchAsync($"/api/projects/{pid}/requirements/REQ-009", new
        {
            baseVersion = 1,
            acceptanceCriteria = new object[] { new { text = "Tek komutla dağıtılabilir" } },
        });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        var req = (await u.GetAsync($"/api/projects/{pid}/versions/2"))["model"].GetProperty("requirements").EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == "REQ-009");
        Assert.Equal("AC-008", req.GetProperty("acceptanceCriteria")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Rejecting_a_proposal_leaves_the_model_untouched_and_cannot_be_accepted_later()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        var rej = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/reject", new { note = "Şimdilik gerek yok" });
        Assert.True(rej.Is(HttpStatusCode.OK), rej.ToString());
        Assert.Equal("rejected", rej.Str("status"));

        var acc = await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        Assert.True(acc.Is(HttpStatusCode.Conflict), acc.ToString());
        Assert.Equal("proposal_rejected", acc.Str("code"));
        Assert.Equal(1, await u.HeadAsync(pid));
    }

    [Fact]
    public async Task Audit_log_records_creation_acceptance_and_edits()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        var prop = await u.PendingProposalAsync(pid);
        await u.PostAsync($"/api/projects/{pid}/proposals/{prop}/accept", new { });
        await u.PatchAsync($"/api/projects/{pid}/open-questions/OQ-001", new { answer = "Hayır" });

        var audit = await u.GetAsync($"/api/projects/{pid}/audit");
        var actions = audit.Body.EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("project.created", actions);
        Assert.Contains("proposal.accepted", actions);
        Assert.Contains("model.edited", actions);
        Assert.All(audit.Body.EnumerateArray(), a => Assert.Equal(u.Email, a.GetProperty("actor").GetString()));
    }

    [Fact]
    public async Task Deleting_a_project_removes_it_for_good()
    {
        var u = await Api.RegisterAsync(factory);
        var pid = await u.CreateSampleAsync();
        Assert.True((await u.DeleteAsync($"/api/projects/{pid}")).Is(HttpStatusCode.NoContent));
        Assert.True((await u.GetAsync($"/api/projects/{pid}")).Is(HttpStatusCode.NotFound));
        Assert.Equal(0, (await u.GetAsync("/api/projects")).Body.GetArrayLength());
    }
}
