namespace Arcvora.Core.Flows;

public enum DelegationStatus { Upcoming, Active, Expired, Revoked }

/// <summary>
/// Vekâlet (yetki devri) dönemi: bir kişi, belirli günler arasında onay görevlerini başkasının yapmasına izin verir. Tarihler çalışma alanının
/// yerel gününe göredir, uçlar dahildir. Vekâlet geçişli değildir: vekil, vekâlet aldığı kişinin kendi görevlerini görür; onun vekil olduğu kişileri değil.
/// </summary>
public static class DelegationRules
{
    public const int MaxDays = 365;
    public const int MaxReason = 200;

    public static IReadOnlyList<FlowIssue> Validate(DateOnly start, DateOnly end, DateOnly today, string? reason)
    {
        var issues = new List<FlowIssue>();
        if (start < today) issues.Add(new FlowIssue("start_in_past", "startsOn", "Başlangıç tarihi bugünden önce olamaz."));
        if (end < start) issues.Add(new FlowIssue("end_before_start", "endsOn", "Bitiş tarihi başlangıçtan önce olamaz."));
        else if (end.DayNumber - start.DayNumber + 1 > MaxDays) issues.Add(new FlowIssue("period_too_long", "endsOn", $"Vekâlet en fazla {MaxDays} gün sürebilir."));
        if (start.DayNumber - today.DayNumber > MaxDays) issues.Add(new FlowIssue("start_too_far", "startsOn", "Başlangıç tarihi bir yıldan fazla ileride olamaz."));
        if (reason is { Length: > MaxReason }) issues.Add(new FlowIssue("reason_too_long", "reason", $"Açıklama en fazla {MaxReason} karakter olabilir."));
        return issues;
    }

    public static DelegationStatus StatusOf(DateOnly start, DateOnly end, bool revoked, DateOnly today) =>
        revoked ? DelegationStatus.Revoked : today < start ? DelegationStatus.Upcoming : today > end ? DelegationStatus.Expired : DelegationStatus.Active;

    public static bool Overlaps(DateOnly s1, DateOnly e1, DateOnly s2, DateOnly e2) => s1 <= e2 && s2 <= e1;

    /// <summary>Çalışma alanının yerel günü: UTC + sabit fark (saat dilimi verisi gerektirmez; Türkiye UTC+3 için 180).</summary>
    public static DateOnly Today(DateTimeOffset now, int utcOffsetMinutes) =>
        DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromMinutes(utcOffsetMinutes)).DateTime);
}
