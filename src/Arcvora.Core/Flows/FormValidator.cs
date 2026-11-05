using System.Globalization;
using System.Text.Json;

namespace Arcvora.Core.Flows;

public sealed record FormResult(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<FlowIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>
/// Talep eden kişinin gönderdiği veriyi tanıma göre doğrular ve normalleştirir. Metinler olduğu gibi (kırpılarak) saklanır;
/// kaçış (escape) işi gösterim katmanındadır. Hesaplanan alanlar istemciden alınmaz, sunucuda üretilir.
/// </summary>
public static class FormValidator
{
    private const decimal MaxNumber = 1_000_000_000_000m;
    private const int DefaultTextLength = 200;

    public static FormResult Validate(FlowDefinition def, IReadOnlyDictionary<string, JsonElement> data)
    {
        var values = new Dictionary<string, object?>();
        var issues = new List<FlowIssue>();
        void Add(string code, string key, string message) => issues.Add(new FlowIssue(code, $"data.{key}", message));

        var known = def.Fields.Select(f => f.Key).ToHashSet();
        foreach (var key in data.Keys.Where(k => !known.Contains(k)))
            Add("unknown_field", key, $"'{key}' adlı bir alan yok.");

        foreach (var f in def.Fields.Where(f => f.Computed is null))
        {
            data.TryGetValue(f.Key, out var el);
            if (IsMissing(f, el))
            {
                if (f.Required) Add("required", f.Key, $"'{f.Label}' zorunlu.");
                continue;
            }
            Parse(f, el, values, Add);
        }

        // 'önce olamaz' kuralları (iki tarih de geçerliyse)
        foreach (var f in def.Fields.Where(f => f.NotBefore is not null))
            if (TryDate(values, f.Key, out var d) && TryDate(values, f.NotBefore!, out var other) && d < other)
            {
                var otherLabel = def.Fields.FirstOrDefault(x => x.Key == f.NotBefore)?.Label ?? f.NotBefore;
                Add("not_before", f.Key, $"'{f.Label}', '{otherLabel}' tarihinden önce olamaz.");
            }

        // hesaplanan alanlar
        foreach (var f in def.Fields.Where(f => f.Computed is not null))
            if (Compute(f.Computed!, values) is { } n) values[f.Key] = n;

        return new FormResult(values, issues);
    }

    /// <summary><c>user</c> tipli alanlarda seçilen kullanıcı kimlikleri (üyelik kontrolü için).</summary>
    public static IReadOnlyList<(string Key, Guid Id)> UserFieldIds(FlowDefinition def, IReadOnlyDictionary<string, object?> values)
    {
        var list = new List<(string, Guid)>();
        foreach (var f in def.Fields.Where(f => f.Type == FieldType.User))
            if (values.TryGetValue(f.Key, out var v) && v is string s && Guid.TryParse(s, out var id)) list.Add((f.Key, id));
        return list;
    }

    /// <summary><c>file</c> tipli alanlarda seçilen ek kimlikleri (sahiplik/varlık kontrolü için).</summary>
    public static IReadOnlyList<(string Key, Guid Id)> FileFieldIds(FlowDefinition def, IReadOnlyDictionary<string, object?> values) => [];

    private static bool IsMissing(FlowField f, JsonElement? el) => el is null || el.Value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(el.Value.GetString()),
        JsonValueKind.Array => f.Type == FieldType.MultiSelect && el.Value.GetArrayLength() == 0,
        _ => false,
    };

    private static void Parse(FlowField f, JsonElement? element, Dictionary<string, object?> values, Action<string, string, string> add)
    {
        var el = element!.Value;
        void Bad(string code, string msg) => add(code, f.Key, msg);

        switch (f.Type)
        {
            case FieldType.Text or FieldType.LongText:
            {
                if (el.ValueKind != JsonValueKind.String) { Bad("invalid_type", $"'{f.Label}' metin olmalı."); return; }
                var s = el.GetString()!.Trim();
                var max = f.MaxLength ?? (f.Type == FieldType.LongText ? FlowValidator.MaxTextLength : DefaultTextLength);
                if (s.Length > max) { Bad("too_long", $"'{f.Label}' en fazla {max} karakter olabilir."); return; }
                values[f.Key] = s;
                break;
            }
            case FieldType.Number or FieldType.Money:
            {
                if (el.ValueKind != JsonValueKind.Number) { Bad("invalid_type", $"'{f.Label}' sayı olmalı."); return; }
                if (!el.TryGetDecimal(out var d) || Math.Abs(d) > MaxNumber) { Bad("out_of_range", $"'{f.Label}' çok büyük."); return; }
                if (f.Type == FieldType.Money && decimal.Round(d, 2) != d) { Bad("too_many_decimals", $"'{f.Label}' en fazla 2 ondalık basamak içerebilir."); return; }
                var min = f.Min ?? (f.Type == FieldType.Money ? 0m : null);
                if (min is { } mn && d < mn || f.Max is { } mx && d > mx) { Bad("out_of_range", $"'{f.Label}' izin verilen aralığın dışında."); return; }
                values[f.Key] = d;
                break;
            }
            case FieldType.Date:
            {
                if (el.ValueKind != JsonValueKind.String) { Bad("invalid_type", $"'{f.Label}' yyyy-AA-gg biçiminde bir tarih olmalı."); return; }
                if (!DateOnly.TryParseExact(el.GetString()!.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year is < 1900 or > 2200)
                { Bad("invalid_date", $"'{f.Label}' geçerli bir tarih olmalı (yyyy-AA-gg)."); return; }
                values[f.Key] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            }
            case FieldType.Select:
            {
                if (el.ValueKind != JsonValueKind.String) { Bad("invalid_type", $"'{f.Label}' bir seçenek olmalı."); return; }
                var s = el.GetString()!.Trim();
                var match = (f.Options ?? []).FirstOrDefault(o => string.Equals(o.Trim(), s, StringComparison.Ordinal));
                if (match is null) { Bad("invalid_option", $"'{f.Label}' için geçersiz seçenek."); return; }
                values[f.Key] = match.Trim();
                break;
            }
            case FieldType.MultiSelect:
            {
                if (el.ValueKind != JsonValueKind.Array) { Bad("invalid_type", $"'{f.Label}' bir seçenek listesi olmalı."); return; }
                var picked = new List<string>();
                foreach (var item in el.EnumerateArray())
                {
                    var s = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
                    var match = s is null ? null : (f.Options ?? []).FirstOrDefault(o => string.Equals(o.Trim(), s, StringComparison.Ordinal));
                    if (match is null) { Bad("invalid_option", $"'{f.Label}' için geçersiz seçenek."); return; }
                    if (!picked.Contains(match.Trim())) picked.Add(match.Trim());
                }
                values[f.Key] = picked;
                break;
            }
            case FieldType.Checkbox:
            {
                if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { Bad("invalid_type", $"'{f.Label}' evet/hayır olmalı."); return; }
                var b = el.GetBoolean();
                if (f.Required && !b) { Bad("required", $"'{f.Label}' işaretlenmeli."); return; }
                values[f.Key] = b;
                break;
            }
            case FieldType.User:
            {
                if (el.ValueKind != JsonValueKind.String) { Bad("invalid_type", $"'{f.Label}' bir kullanıcı olmalı."); return; }
                if (!Guid.TryParse(el.GetString(), out var id) || id == Guid.Empty) { Bad("invalid_user", $"'{f.Label}' için geçersiz kullanıcı."); return; }
                values[f.Key] = id.ToString();
                break;
            }
        }
    }

    private static bool TryDate(IReadOnlyDictionary<string, object?> values, string key, out DateOnly d)
    {
        d = default;
        return values.TryGetValue(key, out var v) && v is string s
               && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    }

    private static decimal? Compute(FlowComputed c, IReadOnlyDictionary<string, object?> values)
    {
        if (!TryDate(values, c.From, out var from) || !TryDate(values, c.To, out var to) || to < from) return null;
        var span = to.DayNumber - from.DayNumber + 1;
        if (c.Op == ComputedOp.CalendarDays) return span;
        if (span > 3660) return null;       // makul olmayan aralıklarda döngüyü sınırla
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        return count;
    }
}
