namespace Arcvora.Core.Flows;

public sealed record Requester(Guid UserId, Guid? ManagerId);

/// <summary>Onaylayıcıları çözmek için gereken kurum bilgisi (veritabanından hizmet katmanı doldurur).</summary>
public interface IFlowDirectory
{
    IReadOnlyList<Guid> GroupMembers(Guid groupId);
    IReadOnlyList<Guid> Admins();
}

/// <param name="FallbackReason">Onaylayıcı doğrudan çözülemediyse nedeni (manager_missing, group_empty, only_approver_is_requester…).</param>
public sealed record StepPlan(int Index, string StepId, string Name, ApprovalMode Mode, IReadOnlyList<Guid> Assignees, string? FallbackReason);

public sealed record SkippedStep(int Index, string StepId, string Name);

public enum AdvanceKind { AwaitApproval, Completed }

public sealed record Advance(AdvanceKind Kind, StepPlan? Plan, IReadOnlyList<SkippedStep> Skipped);

public static class FlowEngine
{
    /// <summary><paramref name="fromIndex"/> dahil olmak üzere koşulu sağlanan ilk adımı bulur; kalmadıysa akış tamamlanmıştır.</summary>
    public static Advance Advance(FlowDefinition def, int fromIndex, IReadOnlyDictionary<string, object?> values, Requester requester, IFlowDirectory directory) =>
        throw new NotImplementedException();

    public static bool ConditionsPass(FlowStep step, IReadOnlyDictionary<string, object?> values) => throw new NotImplementedException();
}

public enum Decision { Approve, Reject }

public enum ApprovalStatus { Pending, Approved, Rejected, Skipped }

public sealed record TaskState(Guid Id, Guid AssigneeId, ApprovalStatus Status);

public enum StepOutcome { Pending, Approved, Rejected }

/// <param name="TasksToSkip">Adım sonuçlandığında artık gerekmeyen, hâlâ bekleyen görevler.</param>
public sealed record StepDecision(StepOutcome Outcome, IReadOnlyList<Guid> TasksToSkip);

public static class StepEvaluator
{
    public static StepDecision Decide(ApprovalMode mode, IReadOnlyList<TaskState> tasks, Guid taskId, Decision decision) =>
        throw new NotImplementedException();
}
