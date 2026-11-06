using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arcvora.Core.Flows;

/// <summary>Akış tanımını yayınlamadan önce doğrular (kimlik/üyelik varlığı hariç; onu hizmet katmanı kontrol eder).</summary>
public static partial class FlowValidator
{
    public const int MaxFields = 60;
    public const int MaxSteps = 10;
    public const int MaxSummaryFields = 3;
    public const int MaxOptions = 50;
    public const int MaxTextLength = 4000;
    public const int MaxFilesPerField = 10;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}$")] private static partial Regex FieldKeyRx();
    [GeneratedRegex("^[a-z][a-z0-9_]{0,19}$")] private static partial Regex StepIdRx();
    [GeneratedRegex("^[A-Z0-9]{1,10}$")] private static partial Regex PrefixRx();

    public static IReadOnlyList<FlowIssue> Validate(FlowDefinition def)
    {
        var issues = new List<FlowIssue>();
        void Add(string code, string path, string message) => issues.Add(new FlowIssue(code, path, message));

        if (def.Schema != 1) Add("unsupported_schema", "schema", "Desteklenmeyen şema sürümü.");
        if (!PrefixRx().IsMatch(def.Prefix ?? "")) Add("invalid_prefix", "prefix", "Önek 1–10 büyük harf veya rakam olmalı (ör. IZN).");

        // ---- alanlar
        if (def.Fields.Count == 0) Add("fields_required", "fields", "En az bir alan gerekli.");
        if (def.Fields.Count > MaxFields) Add("too_many_fields", "fields", $"En fazla {MaxFields} alan tanımlanabilir.");

        var byKey = new Dictionary<string, FlowField>();
        for (var i = 0; i < def.Fields.Count; i++)
        {
            var f = def.Fields[i];
            var p = $"fields[{i}]";
            if (!FieldKeyRx().IsMatch(f.Key ?? "")) Add("invalid_key", $"{p}.key", "Anahtar küçük harfle başlamalı; yalnızca a-z, 0-9 ve _ içermeli (en fazla 40).");
            else if (!byKey.TryAdd(f.Key!, f)) Add("duplicate_key", $"{p}.key", $"'{f.Key}' anahtarı birden fazla alanda kullanılmış.");
            if (string.IsNullOrWhiteSpace(f.Label) || f.Label.Length > 100) Add("invalid_name", $"{p}.label", "Etiket 1–100 karakter olmalı.");
        }

        for (var i = 0; i < def.Fields.Count; i++)
        {
            var f = def.Fields[i];
            var p = $"fields[{i}]";
            var hasList = f.ListId is { } lid && lid != Guid.Empty;
            if (f.ListId is not null && f.Type is not (FieldType.Select or FieldType.MultiSelect))
                Add("invalid_list", $"{p}.listId", "Liste yalnızca tek/çoklu seçim alanlarına bağlanabilir.");
            if (f.Type is FieldType.Select or FieldType.MultiSelect && hasList)
            {
                if ((f.Options ?? []).Count > 0) Add("options_conflict", $"{p}.options", "Seçenekler ve liste birlikte kullanılamaz; birini seçin.");
            }
            else if (f.Type is FieldType.Select or FieldType.MultiSelect)
            {
                var opts = f.Options ?? [];
                if (opts.Count == 0 || opts.Any(o => string.IsNullOrWhiteSpace(o))) Add("options_required", $"{p}.options", "Seçim alanı için boş olmayan seçenekler gerekli.");
                else
                {
                    if (opts.Count > MaxOptions) Add("too_many_options", $"{p}.options", $"En fazla {MaxOptions} seçenek tanımlanabilir.");
                    if (opts.Select(o => o.Trim()).Distinct(StringComparer.Ordinal).Count() != opts.Count) Add("duplicate_option", $"{p}.options", "Seçenekler benzersiz olmalı.");
                    if (opts.Any(o => o.Length > 100)) Add("invalid_name", $"{p}.options", "Seçenek en fazla 100 karakter olabilir.");
                }
            }
            if (f.Type is FieldType.Number or FieldType.Money && f.Min is { } mn && f.Max is { } mx && mn > mx)
                Add("invalid_range", $"{p}.min", "En küçük değer en büyük değerden büyük olamaz.");
            if (f.MaxFiles is { } mf && (f.Type != FieldType.File || mf < 1 || mf > MaxFilesPerField))
                Add("invalid_range", $"{p}.maxFiles", $"Dosya sayısı yalnızca ek alanında ve 1–{MaxFilesPerField} arasında verilebilir.");
            if (f.Type is FieldType.Text or FieldType.LongText && f.MaxLength is { } ml && (ml < 1 || ml > MaxTextLength))
                Add("invalid_range", $"{p}.maxLength", $"En fazla uzunluk 1–{MaxTextLength} olmalı.");

            if (f.NotBefore is not null)
            {
                if (f.Type != FieldType.Date || f.NotBefore == f.Key || !byKey.TryGetValue(f.NotBefore, out var other) || other.Type != FieldType.Date)
                    Add("invalid_reference", $"{p}.notBefore", "'Önce olamaz' yalnızca başka bir tarih alanına bağlanabilir.");
            }
            if (f.Computed is { } c)
            {
                var ok = f.Type == FieldType.Number
                         && byKey.TryGetValue(c.From ?? "", out var from) && from.Type == FieldType.Date
                         && byKey.TryGetValue(c.To ?? "", out var to) && to.Type == FieldType.Date;
                if (!ok) Add("invalid_computed", $"{p}.computed", "Hesaplanan alan bir sayı olmalı ve iki tarih alanından hesaplanmalı.");
                if (f.Required) Add("computed_required", $"{p}.required", "Hesaplanan alan zorunlu olamaz; değeri sunucu üretir.");
            }
        }

        // ---- özet alanları
        if (def.SummaryFields.Count > MaxSummaryFields) Add("too_many_summary_fields", "summaryFields", $"En fazla {MaxSummaryFields} özet alanı seçilebilir.");
        for (var i = 0; i < def.SummaryFields.Count; i++)
            if (!byKey.ContainsKey(def.SummaryFields[i])) Add("unknown_field", $"summaryFields[{i}]", $"'{def.SummaryFields[i]}' adlı alan yok.");

        // ---- adımlar
        if (def.Steps.Count == 0) Add("steps_required", "steps", "En az bir onay adımı gerekli.");
        if (def.Steps.Count > MaxSteps) Add("too_many_steps", "steps", $"En fazla {MaxSteps} adım tanımlanabilir.");
        var stepIds = new HashSet<string>();
        for (var i = 0; i < def.Steps.Count; i++)
        {
            var s = def.Steps[i];
            var p = $"steps[{i}]";
            if (!StepIdRx().IsMatch(s.Id ?? "")) Add("invalid_key", $"{p}.id", "Adım kimliği küçük harfle başlamalı; yalnızca a-z, 0-9 ve _ içermeli (en fazla 20).");
            else if (!stepIds.Add(s.Id!)) Add("duplicate_step_id", $"{p}.id", $"'{s.Id}' kimliği birden fazla adımda kullanılmış.");
            if (string.IsNullOrWhiteSpace(s.Name) || s.Name.Length > 100) Add("invalid_name", $"{p}.name", "Adım adı 1–100 karakter olmalı.");

            var a = s.Approvers;
            if (a.Type is ApproverType.User or ApproverType.Group && (a.Id is null || a.Id == Guid.Empty))
                Add("approver_id_required", $"{p}.approvers.id", "Kişi veya grup seçilmeli.");
            if (a.Type is ApproverType.Manager or ApproverType.Admin && a.Id is not null)
                Add("approver_id_unexpected", $"{p}.approvers.id", "Bu onaylayıcı türünde kimlik verilmez.");

            for (var j = 0; j < s.Conditions.Count; j++) ValidateCondition(s.Conditions[j], $"{p}.conditions[{j}]", byKey, Add);
        }

        // ---- tamamlanma
        if (def.Completion?.Accounting is { Enabled: true } acc && (string.IsNullOrWhiteSpace(acc.Category) || acc.Category.Length > 40))
            Add("category_required", "completion.accounting.category", "Muhasebe kategorisi (en fazla 40 karakter) gerekli.");

        return issues;
    }

    private static void ValidateCondition(FlowCondition c, string path, Dictionary<string, FlowField> byKey, Action<string, string, string> add)
    {
        if (!byKey.TryGetValue(c.Field ?? "", out var f))
        {
            add("unknown_field", $"{path}.field", $"'{c.Field}' adlı alan yok.");
            return;
        }

        // Alan türüne göre izin verilen işleçler
        var allowed = f.Type switch
        {
            FieldType.Number or FieldType.Money or FieldType.Date => true,
            FieldType.Text or FieldType.Select or FieldType.User => c.Op is ConditionOp.Eq or ConditionOp.Ne or ConditionOp.In,
            FieldType.Checkbox => c.Op is ConditionOp.Eq or ConditionOp.Ne,
            FieldType.MultiSelect => c.Op == ConditionOp.In,
            _ => false,      // long_text
        };
        if (!allowed)
        {
            add("invalid_condition", $"{path}.op", "Bu alan türü için bu koşul kullanılamaz.");
            return;
        }

        bool ScalarOk(JsonElement e) => f.Type switch
        {
            FieldType.Number or FieldType.Money => e.ValueKind == JsonValueKind.Number,
            FieldType.Date => e.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(e.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            FieldType.Checkbox => e.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => e.ValueKind == JsonValueKind.String,
        };

        var v = c.Value;
        var good = v is { } val && (c.Op == ConditionOp.In
            ? val.ValueKind == JsonValueKind.Array && val.GetArrayLength() > 0 && val.EnumerateArray().All(ScalarOk)
            : ScalarOk(val));
        if (!good) add("condition_value", $"{path}.value", "Koşul değeri alan türüne uygun olmalı ('içinde' için dolu bir liste).");
    }
}
