using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

/// <summary>Resmî tatil takvimi, iş günü hesabı, kıdeme göre yıllık izin hakkı ve izin bakiyesi tanımı.</summary>
public class LeaveTests
{
    private static DateOnly D(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd");

    private static HolidayCalendar Cal(params (string Date, bool Half)[] days) => new(days.ToDictionary(d => D(d.Date), d => d.Half));

    // ------------------------------------------------------------- iş günü

    [Fact]
    public void Work_days_skip_weekends_and_full_holidays()
    {
        var cal = Cal(("2026-10-29", false));                         // Perşembe: Cumhuriyet Bayramı
        // 27 Eki Sal → 2 Kas Pzt: Sal Çar (Per tatil) Cum Cmt Paz Pzt = Sal, Çar, Cum, Pzt = 4 gün
        Assert.Equal(4m, cal.WorkDays(D("2026-10-27"), D("2026-11-02")));
        Assert.Equal(5m, HolidayCalendar.Empty.WorkDays(D("2026-10-27"), D("2026-11-02")));        // takvimsiz: yalnızca hafta içi
    }

    [Fact]
    public void A_half_day_holiday_counts_as_half_a_work_day()
    {
        var cal = Cal(("2026-10-28", true), ("2026-10-29", false));    // arife yarım gün + bayram
        Assert.Equal(0.5m, cal.WorkDays(D("2026-10-28"), D("2026-10-29")));
        Assert.Equal(1.5m, cal.WorkDays(D("2026-10-27"), D("2026-10-29")));       // Sal 1 + Çar 0,5 + Per 0
    }

    [Fact]
    public void Holidays_on_weekends_take_nothing_extra_and_ranges_are_inclusive()
    {
        var cal = Cal(("2026-11-07", false));                           // Cumartesi
        Assert.Equal(5m, cal.WorkDays(D("2026-11-02"), D("2026-11-08")));
        Assert.Equal(1m, cal.WorkDays(D("2026-11-02"), D("2026-11-02")));
        Assert.Equal(0m, cal.WorkDays(D("2026-11-07"), D("2026-11-08")));
    }

    [Fact]
    public void Weight_describes_a_single_day()
    {
        var cal = Cal(("2026-10-28", true), ("2026-10-29", false));
        Assert.Equal(1m, cal.Weight(D("2026-10-27")));
        Assert.Equal(0.5m, cal.Weight(D("2026-10-28")));
        Assert.Equal(0m, cal.Weight(D("2026-10-29")));
        Assert.Equal(0m, cal.Weight(D("2026-10-31")));                  // Cumartesi
    }

    // ----------------------------------------------------- form hesabı (work_days)

    private static FlowDefinition Def() => new()
    {
        Prefix = "IZN", SummaryFields = [],
        Fields =
        [
            Field("start", FieldType.Date, true), Field("end", FieldType.Date, true) with { NotBefore = "start" },
            Field("days", FieldType.Number) with { Computed = new FlowComputed { Op = ComputedOp.WorkDays, From = "start", To = "end" } },
        ],
        Steps = [new FlowStep { Id = "s1", Name = "Y", Approvers = new ApproverSpec { Type = ApproverType.Manager } }],
    };

    [Fact]
    public void The_form_computes_work_days_using_the_calendar()
    {
        var data = Data("""{ "start": "2026-10-27", "end": "2026-11-02" }""");
        Assert.Equal(4m, FormValidator.Validate(Def(), data, null, Cal(("2026-10-29", false))).Values["days"]);
        Assert.Equal(5m, FormValidator.Validate(Def(), data).Values["days"]);                      // takvim verilmezse hafta içi
    }

    [Fact]
    public void Work_days_is_a_valid_computed_operation() => Assert.Empty(FlowValidator.Validate(Def()));

    [Fact]
    public void Unreasonably_long_ranges_are_not_computed()
    {
        var r = FormValidator.Validate(Def(), Data("""{ "start": "2000-01-01", "end": "2026-01-01" }"""));
        Assert.False(r.Values.ContainsKey("days"));
    }

    // --------------------------------------------------------------- yıllık izin hakkı

    [Theory]
    [InlineData("2020-03-01", 2020, 0)]       // henüz bir yılı doldurmadı
    [InlineData("2020-03-01", 2021, 14)]      // 1. yıl dönümü
    [InlineData("2020-03-01", 2024, 14)]      // 4 yıl
    [InlineData("2020-03-01", 2025, 20)]      // 5 yıl: 20 gün
    [InlineData("2020-03-01", 2034, 20)]      // 14 yıl
    [InlineData("2020-03-01", 2035, 26)]      // 15 yıl: 26 gün
    [InlineData("2020-03-01", 2050, 26)]
    [InlineData("2030-01-01", 2026, 0)]       // henüz işe başlamamış
    [InlineData("2026-12-31", 2026, 0)]
    public void Entitlement_follows_the_service_years_reached_in_the_year(string hired, int year, int days) =>
        Assert.Equal(days, LeaveEntitlement.ForYear(D(hired), year));

    [Fact]
    public void Entitlement_for_a_leap_day_hire_is_defined_for_every_year()
    {
        Assert.Equal(14, LeaveEntitlement.ForYear(D("2020-02-29"), 2021));
        Assert.Equal(14, LeaveEntitlement.ForYear(D("2020-02-29"), 2024));
    }

    // ------------------------------------------------------------------- tanım

    private static FlowDefinition WithLeave(LeaveSpec spec, FlowField? extra = null)
    {
        var d = Def() with { Completion = new FlowCompletion { Leave = spec } };
        return extra is null ? d : d with { Fields = [.. d.Fields, extra] };
    }

    [Fact]
    public void A_valid_leave_balance_definition_passes()
    {
        Assert.Empty(FlowValidator.Validate(WithLeave(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "start" })));
        var typed = WithLeave(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "start", TypeField = "kind", CountsFor = ["Yıllık"] },
            Field("kind", FieldType.Select, true) with { Options = ["Yıllık", "Mazeret"] });
        Assert.Empty(FlowValidator.Validate(typed));
    }

    [Fact]
    public void Disabled_leave_settings_are_ignored() =>
        Assert.Empty(FlowValidator.Validate(WithLeave(new LeaveSpec { Enabled = false, DaysField = "nope" })));

    [Fact]
    public void The_days_and_start_fields_must_exist_with_the_right_types()
    {
        var codes = (LeaveSpec s) => FlowValidator.Validate(WithLeave(s)).Select(i => $"{i.Code}:{i.Path}").ToList();
        Assert.Contains("invalid_leave:completion.leave.daysField", codes(new LeaveSpec { Enabled = true, DaysField = "start", StartField = "start" }));      // sayı değil
        Assert.Contains("invalid_leave:completion.leave.daysField", codes(new LeaveSpec { Enabled = true, DaysField = null, StartField = "start" }));
        Assert.Contains("invalid_leave:completion.leave.startField", codes(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "days" }));     // tarih değil
        Assert.Contains("invalid_leave:completion.leave.startField", codes(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "yok" }));
    }

    [Fact]
    public void The_type_field_and_counted_values_must_agree()
    {
        var typed = (string? typeField, string[]? counts) => FlowValidator.Validate(WithLeave(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "start", TypeField = typeField, CountsFor = counts },
            Field("kind", FieldType.Select, true) with { Options = ["Yıllık", "Mazeret"] })).Select(i => $"{i.Code}:{i.Path}").ToList();
        Assert.Contains("invalid_leave:completion.leave.typeField", typed("start", ["x"]));                 // seçim alanı değil
        Assert.Contains("invalid_leave:completion.leave.countsFor", typed("kind", []));                    // boş
        Assert.Contains("invalid_leave:completion.leave.countsFor", typed("kind", ["Rapor"]));             // seçeneklerde yok
        Assert.Contains("invalid_leave:completion.leave.countsFor", typed(null, ["Yıllık"]));              // tür alanı yokken değer listesi anlamsız
    }

    [Fact]
    public void Leave_balance_spec_survives_a_json_round_trip()
    {
        var def = WithLeave(new LeaveSpec { Enabled = true, DaysField = "days", StartField = "start", TypeField = "kind", CountsFor = ["Yıllık"] });
        var json = Arcvora.Core.Model.ArcvoraJson.Serialize(def);
        Assert.Contains("\"countsFor\"", json);
        var back = System.Text.Json.JsonSerializer.Deserialize<FlowDefinition>(json, Arcvora.Core.Model.ArcvoraJson.Options)!;
        Assert.Equal("days", back.Completion!.Leave!.DaysField);
        Assert.Equal(["Yıllık"], back.Completion.Leave.CountsFor);
    }
}
