using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

public class FlowEngineTests
{
    private static readonly Requester Req = new(Worker, Boss);

    private static IReadOnlyDictionary<string, object?> Values(decimal days) =>
        new Dictionary<string, object?> { ["days"] = days, ["kind"] = "Yıllık", ["start"] = "2026-11-10" };

    [Fact]
    public void First_step_is_assigned_to_the_requesters_manager()
    {
        var a = FlowEngine.Advance(Leave(), 0, Values(3), Req, new Dir());
        Assert.Equal(AdvanceKind.AwaitApproval, a.Kind);
        Assert.Equal("s1", a.Plan!.StepId);
        Assert.Equal([Boss], a.Plan.Assignees);
        Assert.Null(a.Plan.FallbackReason);
        Assert.Empty(a.Skipped);
    }

    [Fact]
    public void A_step_whose_conditions_fail_is_skipped_and_reported()
    {
        // 3 gün: genel müdür adımı (days > 10) atlanır → yönetici onayından sonra akış biter.
        var next = FlowEngine.Advance(Leave(), 1, Values(3), Req, new Dir());
        Assert.Equal(AdvanceKind.Completed, next.Kind);
        Assert.Equal(["s2"], next.Skipped.Select(s => s.StepId));
    }

    [Fact]
    public void A_step_whose_conditions_pass_runs_with_group_members()
    {
        var dir = new Dir(null, (GmGroup, [Gm1, Gm2]));
        var next = FlowEngine.Advance(Leave(), 1, Values(12), Req, dir);
        Assert.Equal(AdvanceKind.AwaitApproval, next.Kind);
        Assert.Equal("s2", next.Plan!.StepId);
        Assert.Equal([Gm1, Gm2], next.Plan.Assignees);
        Assert.Equal(ApprovalMode.Any, next.Plan.Mode);
    }

    [Fact]
    public void Advancing_past_the_last_step_completes_the_flow() =>
        Assert.Equal(AdvanceKind.Completed, FlowEngine.Advance(Leave(), 2, Values(12), Req, new Dir()).Kind);

    [Fact]
    public void Missing_manager_falls_back_to_admins_and_says_why()
    {
        var a = FlowEngine.Advance(Leave(), 0, Values(3), new Requester(Worker, null), new Dir());
        Assert.Equal([Admin1, Admin2], a.Plan!.Assignees);
        Assert.Equal("manager_missing", a.Plan.FallbackReason);
    }

    [Fact]
    public void The_requester_never_approves_their_own_request_when_someone_else_can()
    {
        // Çalışanın yöneticisi kendisi ise (veri hatası) → adminlere düşer.
        var a = FlowEngine.Advance(Leave(), 0, Values(3), new Requester(Boss, Boss), new Dir());
        Assert.DoesNotContain(Boss, a.Plan!.Assignees);
        Assert.Equal("only_approver_is_requester", a.Plan.FallbackReason);
        // Grup üyesi talep edense kendisi çıkarılır.
        var dir = new Dir(null, (GmGroup, [Gm1, Gm2]));
        var b = FlowEngine.Advance(Leave(), 1, Values(12), new Requester(Gm1, Boss), dir);
        Assert.Equal([Gm2], b.Plan!.Assignees);
        Assert.Null(b.Plan.FallbackReason);
    }

    [Fact]
    public void An_empty_group_falls_back_to_admins()
    {
        var a = FlowEngine.Advance(Leave(), 1, Values(12), Req, new Dir());
        Assert.Equal([Admin1, Admin2], a.Plan!.Assignees);
        Assert.Equal("group_empty", a.Plan.FallbackReason);
    }

    [Fact]
    public void If_the_only_admin_is_the_requester_they_are_allowed_but_it_is_flagged()
    {
        var def = Leave() with { Steps = [Leave().Steps[0] with { Approvers = new ApproverSpec { Type = ApproverType.Admin } }] };
        var a = FlowEngine.Advance(def, 0, Values(3), new Requester(Admin1, null), new Dir([Admin1]));
        Assert.Equal([Admin1], a.Plan!.Assignees);
        Assert.Equal("only_approver_is_requester", a.Plan.FallbackReason);
    }

    [Fact]
    public void Specific_user_approver_is_used_unless_it_is_the_requester()
    {
        var def = Leave() with { Steps = [Leave().Steps[0] with { Approvers = new ApproverSpec { Type = ApproverType.User, Id = Gm1 } }] };
        Assert.Equal([Gm1], FlowEngine.Advance(def, 0, Values(3), Req, new Dir()).Plan!.Assignees);
        var self = FlowEngine.Advance(def, 0, Values(3), new Requester(Gm1, null), new Dir());
        Assert.Equal([Admin1, Admin2], self.Plan!.Assignees);
    }

    [Fact]
    public void A_flow_with_every_step_skipped_completes_immediately()
    {
        var def = Leave() with { Steps = [Leave().Steps[1]] };
        var a = FlowEngine.Advance(def, 0, Values(3), Req, new Dir());
        Assert.Equal(AdvanceKind.Completed, a.Kind);
        Assert.Single(a.Skipped);
    }

    [Theory]
    [InlineData(ConditionOp.Eq, "5", 5, true)]
    [InlineData(ConditionOp.Eq, "5", 6, false)]
    [InlineData(ConditionOp.Ne, "5", 6, true)]
    [InlineData(ConditionOp.Gt, "5", 6, true)]
    [InlineData(ConditionOp.Gt, "5", 5, false)]
    [InlineData(ConditionOp.Gte, "5", 5, true)]
    [InlineData(ConditionOp.Lt, "5", 4, true)]
    [InlineData(ConditionOp.Lte, "5", 5, true)]
    [InlineData(ConditionOp.Lte, "5", 6, false)]
    public void Numeric_conditions(ConditionOp op, string value, int actual, bool expected)
    {
        var step = Leave().Steps[0] with { Conditions = [new FlowCondition { Field = "n", Op = op, Value = Json(value) }] };
        Assert.Equal(expected, FlowEngine.ConditionsPass(step, new Dictionary<string, object?> { ["n"] = (decimal)actual }));
    }

    [Fact]
    public void String_in_date_and_missing_value_conditions()
    {
        FlowStep Step(string f, ConditionOp op, string v) => Leave().Steps[0] with { Conditions = [new FlowCondition { Field = f, Op = op, Value = Json(v) }] };
        var vals = new Dictionary<string, object?> { ["kind"] = "Mazeret", ["start"] = "2026-11-10", ["tags"] = new List<string> { "a", "b" }, ["ok"] = true };

        Assert.True(FlowEngine.ConditionsPass(Step("kind", ConditionOp.In, "[\"Yıllık\",\"Mazeret\"]"), vals));
        Assert.False(FlowEngine.ConditionsPass(Step("kind", ConditionOp.In, "[\"Yıllık\"]"), vals));
        Assert.True(FlowEngine.ConditionsPass(Step("kind", ConditionOp.Eq, "\"Mazeret\""), vals));
        Assert.True(FlowEngine.ConditionsPass(Step("start", ConditionOp.Gte, "\"2026-11-10\""), vals));
        Assert.False(FlowEngine.ConditionsPass(Step("start", ConditionOp.Lt, "\"2026-11-10\""), vals));
        Assert.True(FlowEngine.ConditionsPass(Step("tags", ConditionOp.In, "[\"b\",\"z\"]"), vals));   // çoklu seçimde kesişim
        Assert.True(FlowEngine.ConditionsPass(Step("ok", ConditionOp.Eq, "true"), vals));
        // Değeri olmayan alan: eşitlik/karşılaştırma yanlış, "eşit değil" doğru.
        Assert.False(FlowEngine.ConditionsPass(Step("yok", ConditionOp.Eq, "1"), vals));
        Assert.False(FlowEngine.ConditionsPass(Step("yok", ConditionOp.Gt, "1"), vals));
        Assert.True(FlowEngine.ConditionsPass(Step("yok", ConditionOp.Ne, "1"), vals));
    }

    [Fact]
    public void All_conditions_must_hold()
    {
        var step = Leave().Steps[0] with
        {
            Conditions =
            [
                new FlowCondition { Field = "days", Op = ConditionOp.Gt, Value = Json("2") },
                new FlowCondition { Field = "kind", Op = ConditionOp.Eq, Value = Json("\"Ücretsiz\"") },
            ],
        };
        Assert.False(FlowEngine.ConditionsPass(step, Values(5)));       // tür "Yıllık"
        Assert.True(FlowEngine.ConditionsPass(step, new Dictionary<string, object?> { ["days"] = 5m, ["kind"] = "Ücretsiz" }));
    }
}
