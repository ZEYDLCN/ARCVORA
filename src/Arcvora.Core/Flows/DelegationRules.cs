namespace Arcvora.Core.Flows;

public enum DelegationStatus { Upcoming, Active, Expired, Revoked }

/// <summary>İskelet (henüz uygulanmadı).</summary>
public static class DelegationRules
{
    public const int MaxDays = 365;
    public const int MaxReason = 200;
    public static IReadOnlyList<FlowIssue> Validate(DateOnly start, DateOnly end, DateOnly today, string? reason) => [];
    public static DelegationStatus StatusOf(DateOnly start, DateOnly end, bool revoked, DateOnly today) => DelegationStatus.Active;
    public static bool Overlaps(DateOnly s1, DateOnly e1, DateOnly s2, DateOnly e2) => false;
    public static DateOnly Today(DateTimeOffset now, int utcOffsetMinutes) => default;
}
