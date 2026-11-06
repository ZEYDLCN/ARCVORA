namespace Arcvora.Core.Flows;

/// <summary>İskelet (henüz uygulanmadı).</summary>
public sealed record HolidayCalendar(IReadOnlyDictionary<DateOnly, bool> Days)
{
    public static readonly HolidayCalendar Empty = new(new Dictionary<DateOnly, bool>());
    public decimal Weight(DateOnly day) => 0;
    public decimal WorkDays(DateOnly from, DateOnly to) => 0;
}

public static class LeaveEntitlement
{
    public static int ForYear(DateOnly hired, int year) => -1;
}
