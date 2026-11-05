using Arcvora.Core.Flows;

namespace Arcvora.Core.Tests;

public class StepEvaluatorTests
{
    private static readonly Guid T1 = Guid.NewGuid(), T2 = Guid.NewGuid(), T3 = Guid.NewGuid();
    private static TaskState P(Guid id) => new(id, Guid.NewGuid(), ApprovalStatus.Pending);
    private static TaskState A(Guid id) => new(id, Guid.NewGuid(), ApprovalStatus.Approved);

    [Fact]
    public void Any_mode_first_approval_passes_the_step_and_skips_the_rest()
    {
        var d = StepEvaluator.Decide(ApprovalMode.Any, [P(T1), P(T2), P(T3)], T2, ApprovalDecision.Approve);
        Assert.Equal(StepOutcome.Approved, d.Outcome);
        Assert.Equal([T1, T3], d.TasksToSkip.OrderBy(x => x == T1 ? 0 : 1));
    }

    [Fact]
    public void All_mode_waits_for_everyone()
    {
        var first = StepEvaluator.Decide(ApprovalMode.All, [P(T1), P(T2)], T1, ApprovalDecision.Approve);
        Assert.Equal(StepOutcome.Pending, first.Outcome);
        Assert.Empty(first.TasksToSkip);

        var last = StepEvaluator.Decide(ApprovalMode.All, [A(T1), P(T2)], T2, ApprovalDecision.Approve);
        Assert.Equal(StepOutcome.Approved, last.Outcome);
    }

    [Theory]
    [InlineData(ApprovalMode.Any)]
    [InlineData(ApprovalMode.All)]
    public void Any_rejection_rejects_the_step_and_skips_remaining_tasks(ApprovalMode mode)
    {
        var d = StepEvaluator.Decide(mode, [P(T1), P(T2), A(T3)], T1, ApprovalDecision.Reject);
        Assert.Equal(StepOutcome.Rejected, d.Outcome);
        Assert.Equal([T2], d.TasksToSkip);
    }

    [Fact]
    public void Deciding_a_task_that_is_not_pending_is_an_error()
    {
        Assert.Throws<InvalidOperationException>(() => StepEvaluator.Decide(ApprovalMode.Any, [A(T1), P(T2)], T1, ApprovalDecision.Approve));
        Assert.Throws<InvalidOperationException>(() => StepEvaluator.Decide(ApprovalMode.Any, [P(T1)], Guid.NewGuid(), ApprovalDecision.Approve));
    }

    [Fact]
    public void A_single_assignee_step_is_decided_by_that_one_decision()
    {
        Assert.Equal(StepOutcome.Approved, StepEvaluator.Decide(ApprovalMode.All, [P(T1)], T1, ApprovalDecision.Approve).Outcome);
        Assert.Equal(StepOutcome.Rejected, StepEvaluator.Decide(ApprovalMode.Any, [P(T1)], T1, ApprovalDecision.Reject).Outcome);
    }
}
