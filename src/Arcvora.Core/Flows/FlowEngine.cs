using System.Globalization;
using System.Text.Json;

namespace Arcvora.Core.Flows;

public sealed record Requester(Guid UserId, Guid? ManagerId);

/// <summary>Onaylayıcıları çözmek için gereken kurum bilgisi (veritabanından hizmet katmanı doldurur).</summary>
public interface IFlowDirectory
{
    IReadOnlyList<Guid> GroupMembers(Guid groupId);
    IReadOnlyList<Guid> Admins();
}

/// <param name="FallbackReason">Onaylayıcı doğrudan çözülemediyse nedeni (manager_missing, group_empty, user_missing, only_approver_is_requester…).</param>
public sealed record StepPlan(int Index, string StepId, string Name, ApprovalMode Mode, IReadOnlyList<Guid> Assignees, string? FallbackReason);

public sealed record SkippedStep(int Index, string StepId, string Name);

public enum AdvanceKind { AwaitApproval, Completed }

public sealed record Advance(AdvanceKind Kind, StepPlan? Plan, IReadOnlyList<SkippedStep> Skipped);

/// <summary>Saf (veritabanından bağımsız) akış kuralları: hangi adım çalışır, kime atanır.</summary>
public static class FlowEngine
{
    /// <summary><paramref name="fromIndex"/> dahil olmak üzere koşulu sağlanan ilk adımı bulur; kalmadıysa akış tamamlanmıştır.</summary>
    public static Advance Advance(FlowDefinition def, int fromIndex, IReadOnlyDictionary<string, object?> values, Requester requester, IFlowDirectory directory)
    {
        var skipped = new List<SkippedStep>();
        for (var i = Math.Max(0, fromIndex); i < def.Steps.Count; i++)
        {
            var step = def.Steps[i];
            if (!ConditionsPass(step, values))
            {
                skipped.Add(new SkippedStep(i, step.Id, step.Name));
                continue;
            }
            return new Advance(AdvanceKind.AwaitApproval, Resolve(i, step, requester, directory), skipped);
        }
        return new Advance(AdvanceKind.Completed, null, skipped);
    }

    // ------------------------------------------------------------ onaylayıcı çözümü

    private static StepPlan Resolve(int index, FlowStep step, Requester requester, IFlowDirectory dir)
    {
        var a = step.Approvers;
        IReadOnlyList<Guid> candidates = a.Type switch
        {
            ApproverType.Manager => requester.ManagerId is { } m ? [m] : [],
            ApproverType.User => a.Id is { } u ? [u] : [],
            ApproverType.Group => a.Id is { } g ? dir.GroupMembers(g) : [],
            _ => dir.Admins(),
        };

        // Talep eden kendi talebini onaylamaz.
        var others = candidates.Where(c => c != requester.UserId).Distinct().ToList();
        if (others.Count > 0) return new StepPlan(index, step.Id, step.Name, step.Mode, others, null);

        var reason = candidates.Count > 0 ? "only_approver_is_requester" : a.Type switch
        {
            ApproverType.Manager => "manager_missing",
            ApproverType.Group => "group_empty",
            ApproverType.User => "user_missing",
            _ => "admin_missing",
        };

        // Geri düşüş: master admin'ler (talep edeni hariç tut). Başka kimse yoksa talep eden onaylayabilir; bu açıkça işaretlenir.
        var admins = dir.Admins();
        var adminsNoSelf = admins.Where(x => x != requester.UserId).Distinct().ToList();
        if (adminsNoSelf.Count > 0) return new StepPlan(index, step.Id, step.Name, step.Mode, adminsNoSelf, reason);
        if (admins.Contains(requester.UserId)) return new StepPlan(index, step.Id, step.Name, step.Mode, [requester.UserId], "only_approver_is_requester");
        return new StepPlan(index, step.Id, step.Name, step.Mode, [], "no_approver");
    }

    // ------------------------------------------------------------------- koşullar

    public static bool ConditionsPass(FlowStep step, IReadOnlyDictionary<string, object?> values) =>
        step.Conditions.All(c => Evaluate(c, values));

    private static bool Evaluate(FlowCondition c, IReadOnlyDictionary<string, object?> values)
    {
        values.TryGetValue(c.Field, out var v);
        if (v is null) return c.Op == ConditionOp.Ne;      // değeri olmayan alan: yalnızca "eşit değil" doğru
        var j = c.Value;
        if (j is null) return false;

        return c.Op switch
        {
            ConditionOp.Eq => Equal(v, j.Value),
            ConditionOp.Ne => !Equal(v, j.Value),
            ConditionOp.In => j.Value.ValueKind == JsonValueKind.Array && j.Value.EnumerateArray().Any(item => EqualOrOverlap(v, item)),
            _ => Compare(v, j.Value) is { } cmp && c.Op switch
            {
                ConditionOp.Gt => cmp > 0,
                ConditionOp.Gte => cmp >= 0,
                ConditionOp.Lt => cmp < 0,
                _ => cmp <= 0,
            },
        };
    }

    private static bool EqualOrOverlap(object v, JsonElement item) =>
        v is IEnumerable<string> list ? item.ValueKind == JsonValueKind.String && list.Contains(item.GetString(), StringComparer.Ordinal) : Equal(v, item);

    private static bool TryDecimal(object v, out decimal d)
    {
        switch (v)
        {
            case decimal x: d = x; return true;
            case int x: d = x; return true;
            case long x: d = x; return true;
            case double x when double.IsFinite(x): d = (decimal)x; return true;
            default: d = 0; return false;
        }
    }

    private static bool Equal(object v, JsonElement j)
    {
        if (TryDecimal(v, out var d)) return j.ValueKind == JsonValueKind.Number && j.TryGetDecimal(out var jd) && jd == d;
        return v switch
        {
            string s => j.ValueKind == JsonValueKind.String && string.Equals(s, j.GetString(), StringComparison.Ordinal),
            bool b => j.ValueKind is JsonValueKind.True or JsonValueKind.False && j.GetBoolean() == b,
            _ => false,
        };
    }

    private static int? Compare(object v, JsonElement j)
    {
        if (TryDecimal(v, out var d))
            return j.ValueKind == JsonValueKind.Number && j.TryGetDecimal(out var jd) ? d.CompareTo(jd) : null;
        if (v is string s && j.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var a)
            && DateOnly.TryParseExact(j.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b))
            return a.CompareTo(b);
        return null;
    }
}

public enum Decision { Approve, Reject }

public enum ApprovalStatus { Pending, Approved, Rejected, Skipped }

public sealed record TaskState(Guid Id, Guid AssigneeId, ApprovalStatus Status);

public enum StepOutcome { Pending, Approved, Rejected }

/// <param name="TasksToSkip">Adım sonuçlandığında artık gerekmeyen, hâlâ bekleyen görevler.</param>
public sealed record StepDecision(StepOutcome Outcome, IReadOnlyList<Guid> TasksToSkip);

/// <summary>Bir adımın görevleri arasında verilen kararın adımı nasıl sonuçlandırdığını hesaplar.</summary>
public static class StepEvaluator
{
    public static StepDecision Decide(ApprovalMode mode, IReadOnlyList<TaskState> tasks, Guid taskId, Decision decision)
    {
        var task = tasks.FirstOrDefault(t => t.Id == taskId) ?? throw new InvalidOperationException("Görev bu adıma ait değil.");
        if (task.Status != ApprovalStatus.Pending) throw new InvalidOperationException("Görev zaten karara bağlanmış.");

        var otherPending = tasks.Where(t => t.Id != taskId && t.Status == ApprovalStatus.Pending).Select(t => t.Id).ToList();
        if (decision == Decision.Reject) return new StepDecision(StepOutcome.Rejected, otherPending);
        if (mode == ApprovalMode.Any) return new StepDecision(StepOutcome.Approved, otherPending);
        return otherPending.Count == 0 ? new StepDecision(StepOutcome.Approved, []) : new StepDecision(StepOutcome.Pending, []);
    }
}
