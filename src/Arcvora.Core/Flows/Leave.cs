namespace Arcvora.Core.Flows;

/// <summary>
/// Çalışma alanının resmî tatil takvimi (tarih → yarım gün mü). İş günü sayımında tam tatil 0, yarım gün (ör. arife) 0,5 sayılır;
/// hafta sonu zaten 0'dır (hafta sonuna denk gelen tatil ayrıca bir şey düşürmez).
/// </summary>
public sealed record HolidayCalendar(IReadOnlyDictionary<DateOnly, bool> Days)
{
    public static readonly HolidayCalendar Empty = new(new Dictionary<DateOnly, bool>());

    public decimal Weight(DateOnly day)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return 0m;
        return Days.TryGetValue(day, out var half) ? (half ? 0.5m : 0m) : 1m;
    }

    /// <summary>[from, to] aralığındaki (uçlar dahil) iş günü sayısı.</summary>
    public decimal WorkDays(DateOnly from, DateOnly to)
    {
        if (to < from) return 0m;
        decimal total = 0m;
        for (var d = from; d <= to; d = d.AddDays(1)) total += Weight(d);
        return total;
    }
}

/// <summary>
/// 4857 sayılı İş Kanunu m.53 (yıllık ücretli izin): hizmet süresi 1 yıldan az ise hak doğmaz; 1–5 yıl (5 hariç) 14 gün; 5–15 yıl (15 hariç) 20 gün;
/// 15 yıl ve üzeri 26 gün. Hak, işe girişin yıl dönümünün düştüğü takvim yılı için hesaplanır (o yıl tamamlanan hizmet yılı). Yaş (18 altı/50 üstü için en az 20 gün)
/// ve sözleşmedeki daha iyi koşullar dikkate alınmaz: bunlar için yönetici bakiyeye düzeltme ekler.
/// </summary>
public static class LeaveEntitlement
{
    public static int ForYear(DateOnly hired, int year)
    {
        var completed = year - hired.Year;
        return completed switch { < 1 => 0, < 5 => 14, < 15 => 20, _ => 26 };
    }
}
