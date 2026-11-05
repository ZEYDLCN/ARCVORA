using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

public class FormValidatorTests
{
    private static FormResult Run(Dictionary<string, System.Text.Json.JsonElement> data, FlowDefinition? def = null) => FormValidator.Validate(def ?? Leave(), data);
    private static void Fails(FormResult r, string code, string path) => Assert.Contains(r.Issues, i => i.Code == code && i.Path == path);

    [Fact]
    public void A_valid_leave_request_is_normalised_and_computes_calendar_days()
    {
        var r = Run(ValidLeaveData());
        Assert.True(r.IsValid, string.Join("; ", r.Issues.Select(i => i.Code + ":" + i.Path)));
        Assert.Equal("2026-11-10", r.Values["start"]);
        Assert.Equal(5m, r.Values["days"]);     // 10–14 Kasım, uçlar dahil
        Assert.Equal("Yıllık", r.Values["kind"]);
        Assert.False(r.Values.ContainsKey("note")); // boş isteğe bağlı alan saklanmaz
    }

    [Fact]
    public void Weekdays_op_skips_saturdays_and_sundays()
    {
        var d = Leave();
        d = d with { Fields = [.. d.Fields.Select(f => f.Key == "days" ? f with { Computed = f.Computed! with { Op = ComputedOp.Weekdays } } : f)] };
        // 2026-11-13 Cuma → 2026-11-17 Salı: Cuma, Pazartesi, Salı = 3 iş günü
        var r = Run(Data("""{ "start": "2026-11-13", "end": "2026-11-17", "kind": "Yıllık" }"""), d);
        Assert.Equal(3m, r.Values["days"]);
    }

    [Fact]
    public void Submitting_a_computed_field_is_ignored_and_recomputed()
    {
        var r = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-14", "kind": "Yıllık", "days": 999 }"""));
        Assert.True(r.IsValid);
        Assert.Equal(5m, r.Values["days"]);
    }

    [Fact]
    public void Missing_required_fields_are_reported_with_their_path()
    {
        var r = Run(Data("""{ "note": "x" }"""));
        Fails(r, "required", "data.start");
        Fails(r, "required", "data.end");
        Fails(r, "required", "data.kind");
    }

    [Fact]
    public void Blank_strings_count_as_missing_for_required_fields()
    {
        var r = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "   " }"""));
        Fails(r, "required", "data.kind");
    }

    [Fact]
    public void Unknown_fields_are_rejected()
    {
        var r = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "hack": 1 }"""));
        Fails(r, "unknown_field", "data.hack");
    }

    [Fact]
    public void Dates_must_be_real_iso_dates_and_not_before_the_referenced_date()
    {
        Fails(Run(Data("""{ "start": "2026-13-40", "end": "2026-11-12", "kind": "Yıllık" }""")), "invalid_date", "data.start");
        Fails(Run(Data("""{ "start": "10.11.2026", "end": "2026-11-12", "kind": "Yıllık" }""")), "invalid_date", "data.start");
        Fails(Run(Data("""{ "start": "2026-11-12", "end": "2026-11-10", "kind": "Yıllık" }""")), "not_before", "data.end");
        Fails(Run(Data("""{ "start": 5, "end": "2026-11-10", "kind": "Yıllık" }""")), "invalid_type", "data.start");
    }

    [Fact]
    public void Select_values_must_be_one_of_the_options() =>
        Fails(Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Hediye" }""")), "invalid_option", "data.kind");

    [Fact]
    public void Text_is_trimmed_and_length_limited()
    {
        var ok = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "note": "  merhaba  " }"""));
        Assert.Equal("merhaba", ok.Values["note"]);
        var long100 = new string('x', 101);
        Fails(Run(Data($$"""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "note": "{{long100}}" }""")), "too_long", "data.note");
    }

    [Fact]
    public void Numbers_must_be_json_numbers_within_range_and_money_has_two_decimals()
    {
        var d = Leave() with
        {
            Fields = [Field("n", FieldType.Number) with { Min = 1, Max = 10 }, Field("amount", FieldType.Money, true)],
            SummaryFields = [], Steps = [Leave().Steps[0]],
        };
        Fails(Run(Data("""{ "n": "5", "amount": 10 }"""), d), "invalid_type", "data.n");
        Fails(Run(Data("""{ "n": 0, "amount": 10 }"""), d), "out_of_range", "data.n");
        Fails(Run(Data("""{ "n": 11, "amount": 10 }"""), d), "out_of_range", "data.n");
        Fails(Run(Data("""{ "n": 5, "amount": 10.555 }"""), d), "too_many_decimals", "data.amount");
        Fails(Run(Data("""{ "n": 5, "amount": -1 }"""), d), "out_of_range", "data.amount");     // para varsayılan olarak negatif olamaz
        var ok = Run(Data("""{ "n": 5, "amount": 1250.5 }"""), d);
        Assert.True(ok.IsValid);
        Assert.Equal(1250.5m, ok.Values["amount"]);
    }

    [Fact]
    public void Multi_select_and_checkbox_rules()
    {
        var d = Leave() with
        {
            Fields = [Field("tags", FieldType.MultiSelect, true) with { Options = ["a", "b", "c"] }, Field("ok", FieldType.Checkbox, true)],
            SummaryFields = [], Steps = [Leave().Steps[0]],
        };
        var good = Run(Data("""{ "tags": ["a", "b", "a"], "ok": true }"""), d);
        Assert.True(good.IsValid);
        Assert.Equal(["a", "b"], (IReadOnlyList<string>)good.Values["tags"]!);

        Fails(Run(Data("""{ "tags": [], "ok": true }"""), d), "required", "data.tags");
        Fails(Run(Data("""{ "tags": ["z"], "ok": true }"""), d), "invalid_option", "data.tags");
        Fails(Run(Data("""{ "tags": ["a"], "ok": false }"""), d), "required", "data.ok");    // zorunlu onay kutusu işaretli olmalı
        Fails(Run(Data("""{ "tags": ["a"], "ok": "evet" }"""), d), "invalid_type", "data.ok");
    }

    [Fact]
    public void User_fields_must_be_guids_and_are_exposed_for_membership_checks()
    {
        var bad = Run(Data($$"""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "proxy": "birisi" }"""));
        Fails(bad, "invalid_user", "data.proxy");

        var ok = Run(Data($$"""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "proxy": "{{Boss.ToString().ToUpperInvariant()}}" }"""));
        Assert.True(ok.IsValid);
        Assert.Equal(Boss.ToString(), ok.Values["proxy"]);      // küçük harfe normalleşir
        var users = FormValidator.UserFieldIds(Leave(), ok.Values);
        Assert.Equal([("proxy", Boss)], users);
    }

    [Fact]
    public void Null_for_an_optional_field_is_dropped_and_for_a_required_one_is_missing()
    {
        var r = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": null, "note": null }"""));
        Fails(r, "required", "data.kind");
        Assert.False(r.Values.ContainsKey("note"));
    }

    [Fact]
    public void Hostile_text_is_kept_verbatim_so_the_ui_can_escape_it()
    {
        var r = Run(Data("""{ "start": "2026-11-10", "end": "2026-11-12", "kind": "Yıllık", "note": "<script>alert(1)</script>" }"""));
        Assert.Equal("<script>alert(1)</script>", r.Values["note"]);
    }
}
