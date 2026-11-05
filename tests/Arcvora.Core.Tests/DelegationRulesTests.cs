using Arcvora.Core.Flows;

namespace Arcvora.Core.Tests;

/// <summary>Vekâlet (yetki devri) dönemi kuralları: doğrulama, durum ve çakışma. Tarihler çalışma alanının yerel gününe göredir.</summary>
public class DelegationRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 5);
    private static DateOnly D(int offset) => Today.AddDays(offset);

    private static string[] Codes(DateOnly start, DateOnly end, string? reason = null) =>
        DelegationRules.Validate(start, end, Today, reason).Select(i => i.Code).ToArray();

    [Fact]
    public void A_period_starting_today_or_later_and_ending_on_or_after_the_start_is_valid()
    {
        Assert.Empty(Codes(D(0), D(0)));            // tek gün
        Assert.Empty(Codes(D(0), D(14)));
        Assert.Empty(Codes(D(30), D(40)));          // ileri tarihli
        Assert.Empty(Codes(D(0), D(364)));          // en uzun: 365 gün (uçlar dahil)
    }

    [Fact]
    public void Past_starts_and_inverted_ranges_are_refused()
    {
        Assert.Contains("start_in_past", Codes(D(-1), D(3)));
        Assert.Contains("end_before_start", Codes(D(5), D(4)));
    }

    [Fact]
    public void Periods_longer_than_a_year_or_starting_too_far_ahead_are_refused()
    {
        Assert.Contains("period_too_long", Codes(D(0), D(365)));
        Assert.Contains("start_too_far", Codes(D(366), D(367)));
    }

    [Fact]
    public void An_overlong_reason_is_refused_but_blank_and_null_are_fine()
    {
        Assert.Contains("reason_too_long", Codes(D(0), D(1), new string('x', 201)));
        Assert.Empty(Codes(D(0), D(1), new string('x', 200)));
        Assert.Empty(Codes(D(0), D(1), "   "));
        Assert.Empty(Codes(D(0), D(1), null));
    }

    [Theory]
    [InlineData(-5, -1, false, DelegationStatus.Expired)]
    [InlineData(-5, 0, false, DelegationStatus.Active)]        // son gün dahil
    [InlineData(0, 3, false, DelegationStatus.Active)]         // ilk gün dahil
    [InlineData(1, 3, false, DelegationStatus.Upcoming)]
    [InlineData(-2, 9, true, DelegationStatus.Revoked)]        // iptal her şeyi geçersiz kılar
    [InlineData(5, 9, true, DelegationStatus.Revoked)]
    public void Status_follows_today_and_revocation(int start, int end, bool revoked, DelegationStatus expected) =>
        Assert.Equal(expected, DelegationRules.StatusOf(D(start), D(end), revoked, Today));

    [Theory]
    [InlineData(0, 5, 5, 9, true)]        // bitiş = başlangıç günü dahil çakışır
    [InlineData(0, 5, 6, 9, false)]
    [InlineData(3, 4, 0, 10, true)]       // içerde
    [InlineData(0, 10, 3, 4, true)]
    [InlineData(6, 9, 0, 5, false)]
    public void Overlap_is_inclusive_on_both_ends(int s1, int e1, int s2, int e2, bool expected) =>
        Assert.Equal(expected, DelegationRules.Overlaps(D(s1), D(e1), D(s2), D(e2)));

    [Fact]
    public void Today_uses_the_configured_utc_offset()
    {
        var late = new DateTimeOffset(2026, 11, 5, 22, 30, 0, TimeSpan.Zero);          // UTC 22:30 → İstanbul (UTC+3) ertesi gün 01:30
        Assert.Equal(new DateOnly(2026, 11, 6), DelegationRules.Today(late, 180));
        Assert.Equal(new DateOnly(2026, 11, 5), DelegationRules.Today(late, 0));
        Assert.Equal(new DateOnly(2026, 11, 5), DelegationRules.Today(late, -300));
    }
}
