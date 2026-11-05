using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcvora.Core.Model;

namespace Arcvora.Core.Flows;

/// <summary>Normalleştirilmiş talep verisinin JSON'a yazılması/okunması, tanım özeti ve talep numarası.</summary>
public static class FlowValues
{
    public static string ToJson(IReadOnlyDictionary<string, object?> values) =>
        JsonSerializer.Serialize(values.Where(kv => kv.Value is not null).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value), ArcvoraJson.Options);

    /// <summary>JSON → tipli değerler: sayı=decimal, metin=string, onay=bool, liste=List&lt;string&gt;.</summary>
    public static IReadOnlyDictionary<string, object?> FromJson(string json)
    {
        var result = new Dictionary<string, object?>();
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            result[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.Number => p.Value.TryGetDecimal(out var d) ? d : null,
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array => p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList(),
                _ => null,
            };
        }
        return result;
    }

    /// <summary>Listelerde gösterilen kısa özet: seçilen alanların değerleri " · " ile birleşir. Kullanıcı alanları atlanır.</summary>
    public static string Summary(FlowDefinition def, IReadOnlyDictionary<string, object?> values, int maxLength = 300)
    {
        var parts = new List<string>();
        foreach (var key in def.SummaryFields)
        {
            var field = def.Fields.FirstOrDefault(f => f.Key == key);
            if (field is null || field.Type == FieldType.User || !values.TryGetValue(key, out var v) || v is null) continue;
            var text = v switch
            {
                string s when field.Type == FieldType.Date && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                string s => s,
                decimal m => m.ToString(field.Type == FieldType.Money ? "0.00" : "0.##", CultureInfo.InvariantCulture) + (field.Type == FieldType.Money ? " " + (field.Currency ?? "TRY") : ""),
                bool b => b ? "Evet" : "Hayır",
                IEnumerable<string> list => string.Join(", ", list),
                _ => v.ToString() ?? "",
            };
            if (text.Length > 0) parts.Add(text);
        }
        var summary = string.Join(" · ", parts);
        return summary.Length <= maxLength ? summary : summary[..(maxLength - 1)] + "…";
    }

    public static string Number(string prefix, int year, int sequence) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}-{year}-{sequence:0000}");

    /// <summary>Tanımın kanonik SHA-256 özeti (küçük harf hex). Aynı içerik → aynı özet; taslak ile yayının karşılaştırması için.</summary>
    public static string Hash(FlowDefinition def) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ArcvoraJson.Serialize(def)))).ToLowerInvariant();
}
