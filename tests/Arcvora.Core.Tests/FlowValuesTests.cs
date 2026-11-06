using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

public class FlowValuesTests
{
    [Fact]
    public void Values_survive_a_json_round_trip_with_their_types()
    {
        var values = new Dictionary<string, object?>
        {
            ["n"] = 5m, ["s"] = "metin", ["b"] = true, ["tags"] = new List<string> { "a", "b" }, ["dropped"] = null,
        };
        var back = FlowValues.FromJson(FlowValues.ToJson(values));
        Assert.Equal(5m, back["n"]);
        Assert.Equal("metin", back["s"]);
        Assert.Equal(true, back["b"]);
        Assert.Equal(["a", "b"], (List<string>)back["tags"]!);
        Assert.False(back.ContainsKey("dropped"));
    }

    [Fact]
    public void ToJson_is_stable_regardless_of_insertion_order() =>
        Assert.Equal(FlowValues.ToJson(new Dictionary<string, object?> { ["b"] = 1m, ["a"] = 2m }), FlowValues.ToJson(new Dictionary<string, object?> { ["a"] = 2m, ["b"] = 1m }));

    [Fact]
    public void Summary_joins_selected_fields_with_readable_formats()
    {
        var r = FormValidator.Validate(Leave(), ValidLeaveData());
        Assert.Equal("10.11.2026 · 14.11.2026 · Yıllık", FlowValues.Summary(Leave(), r.Values));
    }

    [Fact]
    public void Summary_parts_keep_label_kind_and_the_same_readable_text_as_the_summary()
    {
        var def = Leave() with { Fields = [.. Leave().Fields.Select(f => f.Key == "start" ? f with { Label = "Başlangıç" } : f.Key == "end" ? f with { Label = "Bitiş" } : f.Key == "kind" ? f with { Label = "İzin türü" } : f)] };
        var r = FormValidator.Validate(def, ValidLeaveData());
        var parts = FlowValues.SummaryParts(def, r.Values);
        Assert.Equal(["start", "end", "kind"], parts.Select(p => p.Key).ToArray());
        Assert.Equal(["Başlangıç", "Bitiş", "İzin türü"], parts.Select(p => p.Label).ToArray());
        Assert.Equal(["date", "date", "text"], parts.Select(p => p.Kind).ToArray());
        Assert.Equal(["10.11.2026", "14.11.2026", "Yıllık"], parts.Select(p => p.Text).ToArray());
        Assert.Equal(string.Join(" · ", parts.Select(p => p.Text)), FlowValues.Summary(def, r.Values));
    }

    [Fact]
    public void Summary_parts_classify_money_numbers_and_flags_and_skip_users_files_and_missing_values()
    {
        var def = Leave() with
        {
            Fields = [Field("amount", FieldType.Money), Field("n", FieldType.Number), Field("ok", FieldType.Checkbox), Field("who", FieldType.User), Field("t", FieldType.Text)],
            SummaryFields = ["who", "amount", "n", "ok", "t", "yok"],
        };
        var parts = FlowValues.SummaryParts(def, new Dictionary<string, object?> { ["amount"] = 1250.5m, ["n"] = 3m, ["ok"] = true, ["who"] = Guid.NewGuid().ToString(), ["t"] = "" });
        Assert.Equal(["amount", "n", "ok"], parts.Select(p => p.Key).ToArray());
        Assert.Equal(["money", "number", "text"], parts.Select(p => p.Kind).ToArray());
        Assert.Equal(["1250.50 TRY", "3", "Evet"], parts.Select(p => p.Text).ToArray());
    }

    [Fact]
    public void Summary_parts_are_empty_without_summary_fields()
    {
        Assert.Empty(FlowValues.SummaryParts(Leave() with { SummaryFields = [] }, new Dictionary<string, object?> { ["start"] = "2026-11-10" }));
    }

    [Fact]
    public void Summary_formats_money_and_skips_missing_and_user_fields()
    {
        var def = Leave() with
        {
            Fields = [Field("amount", FieldType.Money), Field("who", FieldType.User), Field("ok", FieldType.Checkbox), Field("n", FieldType.Number)],
            SummaryFields = ["who", "amount", "ok", "n", "yok"],
        };
        var s = FlowValues.Summary(def, new Dictionary<string, object?> { ["amount"] = 1250.5m, ["who"] = Guid.NewGuid().ToString(), ["ok"] = false });
        Assert.Equal("1250.50 TRY · Hayır", s);
    }

    [Fact]
    public void Summary_is_truncated()
    {
        var def = Leave() with { Fields = [Field("t", FieldType.Text)], SummaryFields = ["t"] };
        var s = FlowValues.Summary(def, new Dictionary<string, object?> { ["t"] = new string('x', 500) }, 50);
        Assert.Equal(50, s.Length);
        Assert.EndsWith("…", s);
    }

    [Fact]
    public void Request_numbers_are_padded_and_culture_independent() => Assert.Equal("IZN-2026-0007", FlowValues.Number("IZN", 2026, 7));

    [Fact]
    public void Hash_changes_with_content_and_is_stable()
    {
        Assert.Equal(FlowValues.Hash(Leave()), FlowValues.Hash(Leave()));
        Assert.NotEqual(FlowValues.Hash(Leave()), FlowValues.Hash(Leave() with { Prefix = "XYZ" }));
        Assert.Equal(64, FlowValues.Hash(Leave()).Length);
    }
}
