using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

/// <summary>PDF, e-posta ve ekranlarda ortak kullanılan okunur değer biçimi (Türkçe).</summary>
public class FlowDisplayTests
{
    private static string Fmt(FieldType t, object? v, Action<FlowField, Dictionary<Guid, string>>? _ = null, string? currency = null, IReadOnlyDictionary<Guid, string>? people = null) =>
        FlowDisplay.Format(Field("k", t) with { Currency = currency }, v, people ?? new Dictionary<Guid, string>());

    [Theory]
    [InlineData("2026-11-05", "05.11.2026")]
    [InlineData("2026-01-31", "31.01.2026")]
    public void Dates_are_day_month_year(string iso, string shown) => Assert.Equal(shown, Fmt(FieldType.Date, iso));

    [Fact]
    public void Money_uses_turkish_grouping_two_decimals_and_currency()
    {
        Assert.Equal("1.234,50 TRY", Fmt(FieldType.Money, 1234.5m));
        Assert.Equal("0,00 TRY", Fmt(FieldType.Money, 0m));
        Assert.Equal("10.000,00 EUR", Fmt(FieldType.Money, 10000m, currency: "EUR"));
        Assert.Equal("1.000.000,99 TRY", Fmt(FieldType.Money, 1_000_000.99m));
    }

    [Fact]
    public void Numbers_drop_pointless_decimals()
    {
        Assert.Equal("5", Fmt(FieldType.Number, 5m));
        Assert.Equal("2,5", Fmt(FieldType.Number, 2.5m));
        Assert.Equal("1.250", Fmt(FieldType.Number, 1250m));
    }

    [Fact]
    public void Booleans_lists_text_and_files()
    {
        Assert.Equal("Evet", Fmt(FieldType.Checkbox, true));
        Assert.Equal("Hayır", Fmt(FieldType.Checkbox, false));
        Assert.Equal("a, b", Fmt(FieldType.MultiSelect, new List<string> { "a", "b" }));
        Assert.Equal("Yıllık", Fmt(FieldType.Select, "Yıllık"));
        Assert.Equal("2 dosya", Fmt(FieldType.File, new List<string> { "x", "y" }));
        Assert.Equal("1 dosya", Fmt(FieldType.File, new List<string> { "x" }));
    }

    [Fact]
    public void Users_show_their_name_or_a_clear_placeholder()
    {
        var id = Guid.NewGuid();
        Assert.Equal("Ayşe Kaya", Fmt(FieldType.User, id.ToString(), people: new Dictionary<Guid, string> { [id] = "Ayşe Kaya" }));
        Assert.Equal("(bilinmeyen kullanıcı)", Fmt(FieldType.User, Guid.NewGuid().ToString()));
        Assert.Equal("(bilinmeyen kullanıcı)", Fmt(FieldType.User, "not-a-guid"));
    }

    [Fact]
    public void Missing_values_show_a_dash()
    {
        foreach (var t in Enum.GetValues<FieldType>()) Assert.Equal("—", Fmt(t, null));
        Assert.Equal("—", Fmt(FieldType.Text, ""));
        Assert.Equal("—", Fmt(FieldType.File, new List<string>()));
        Assert.Equal("—", Fmt(FieldType.MultiSelect, new List<string>()));
    }

    [Fact]
    public void Unexpected_value_shapes_never_throw()
    {
        Assert.Equal("abc", Fmt(FieldType.Money, "abc"));
        Assert.Equal("5", Fmt(FieldType.Date, 5m).Length > 0 ? "5" : "");
    }
}
