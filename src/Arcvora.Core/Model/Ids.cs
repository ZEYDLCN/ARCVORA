using System.Text.RegularExpressions;

namespace Arcvora.Core.Model;

public static partial class Ids
{
    public const string AcceptancePrefix = "AC";

    private static readonly Dictionary<ObjectType, string> Prefixes = new()
    {
        [ObjectType.Source] = "SRC",
        [ObjectType.Requirement] = "REQ",
        [ObjectType.Assumption] = "ASM",
        [ObjectType.Decision] = "DEC",
        [ObjectType.Component] = "CMP",
        [ObjectType.Relationship] = "REL",
        [ObjectType.Entity] = "ENT",
        [ObjectType.Actor] = "ACT",
        [ObjectType.Flow] = "FLW",
        [ObjectType.Endpoint] = "API",
        [ObjectType.TraceLink] = "TRC",
        [ObjectType.OpenQuestion] = "OQ",
    };

    private static readonly Dictionary<string, ObjectType> ByPrefix =
        Prefixes.ToDictionary(kv => kv.Value, kv => kv.Key);

    [GeneratedRegex(@"^([A-Z]{2,3})-(\d{3,})$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\b(?:SRC|REQ|ASM|DEC|CMP|REL|ENT|ACT|FLW|API|TRC|OQ|AC)-\d{3,}\b")]
    private static partial Regex IdScanPattern();

    public static string Prefix(ObjectType type) => Prefixes[type];

    public static bool IsWellFormed(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>Kimliğin önekine göre nesne türü; bilinmeyen önek için null.</summary>
    public static ObjectType? TypeOf(string? id)
    {
        if (id is null) return null;
        var m = IdPattern().Match(id);
        return m.Success && ByPrefix.TryGetValue(m.Groups[1].Value, out var t) ? t : null;
    }

    public static bool IsAcceptanceCriterionId(string? id)
    {
        if (id is null) return false;
        var m = IdPattern().Match(id);
        return m.Success && m.Groups[1].Value == AcceptancePrefix;
    }

    /// <summary>Serbest metin dahil her yerde geçen kimlikler (ör. gerekçedeki "OQ-004" anması dahil).</summary>
    public static IEnumerable<string> ScanForIds(string text) =>
        IdScanPattern().Matches(text).Select(m => m.Value);

    /// <summary>
    /// Yalnızca değeri tamamen bir nesne kimliği olan JSON alanları (yapısal başvurular). Serbest metindeki anmalar
    /// ve iç içe kabul ölçütü kimlikleri (AC-) sayılmaz.
    /// </summary>
    public static IReadOnlyList<string> StructuralRefs(string json)
    {
        var result = new List<string>();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Walk(doc.RootElement, result);
        return result;

        static void Walk(System.Text.Json.JsonElement e, List<string> acc)
        {
            switch (e.ValueKind)
            {
                case System.Text.Json.JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value, acc);
                    break;
                case System.Text.Json.JsonValueKind.Array:
                    foreach (var i in e.EnumerateArray()) Walk(i, acc);
                    break;
                case System.Text.Json.JsonValueKind.String:
                    var s = e.GetString();
                    if (TypeOf(s) is not null) acc.Add(s!);
                    break;
            }
        }
    }

    /// <summary>Mevcut kimlikler arasında en büyük numaranın bir fazlası. Boşluklar yeniden kullanılmaz.</summary>
    public static string Next(string prefix, IEnumerable<string> existing)
    {
        var max = 0;
        foreach (var id in existing)
        {
            var m = IdPattern().Match(id);
            if (m.Success && m.Groups[1].Value == prefix && int.TryParse(m.Groups[2].Value, out var n) && n > max)
                max = n;
        }
        return $"{prefix}-{max + 1:000}";
    }
}
