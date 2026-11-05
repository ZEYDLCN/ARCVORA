using System.Globalization;

namespace Arcvora.Core.Flows;

/// <summary>
/// Kayıtlı bir alan değerini okunur Türkçe metne çevirir (PDF, e-posta ve dışa aktarımlarda ortak). Metinler olduğu gibi döner;
/// kaçış/HTML işi çıktı katmanındadır. Beklenmeyen biçimler istisna fırlatmaz.
/// </summary>
public static class FlowDisplay
{
    public const string Dash = "—";
    public const string UnknownUser = "(bilinmeyen kullanıcı)";

    // Kültür verisine (ICU) bağımlı olmamak için açık biçim: 1.234,50
    private static readonly NumberFormatInfo Tr = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3] };

    public static string Format(FlowField field, object? value, IReadOnlyDictionary<Guid, string> people)
    {
        if (value is null) return Dash;
        switch (field.Type)
        {
            case FieldType.Date when value is string d:
                return DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : Blank(d);
            case FieldType.Money when value is decimal m:
                return m.ToString("N2", Tr) + " " + (field.Currency ?? "TRY");
            case FieldType.Number when value is decimal n:
                return n.ToString("#,##0.##", Tr);
            case FieldType.Checkbox when value is bool b:
                return b ? "Evet" : "Hayır";
            case FieldType.User when value is string u:
                return Guid.TryParse(u, out var id) && people.TryGetValue(id, out var name) ? name : UnknownUser;
            case FieldType.File when value is IEnumerable<string> files:
                var count = files.Count();
                return count == 0 ? Dash : $"{count} dosya";
            case FieldType.MultiSelect when value is IEnumerable<string> list:
                var joined = string.Join(", ", list);
                return Blank(joined);
        }
        return value switch
        {
            string s => Blank(s),
            IEnumerable<string> l => Blank(string.Join(", ", l)),
            _ => Blank(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };
    }

    private static string Blank(string? s) => string.IsNullOrWhiteSpace(s) ? Dash : s;
}
