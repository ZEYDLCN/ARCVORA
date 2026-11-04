using System.Text.Json;
using System.Text.Json.Nodes;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;

namespace Arcvora.Core.Ai;

/// <summary>
/// Üretici çıktısını güvenilmeyen girdi sayar. Kaynak doküman içindeki talimatlar sistem yetkisini değiştiremez (§19):
/// izin verilen nesne türleri, işlem sayısı, kaynak ve onay alanları burada zorlanır.
/// </summary>
/// <param name="Blocking">true: üretici çıktısı geçersiz, onarım gerekir. false: sistem düzeltti, yalnızca not düşülür.</param>
public sealed record GuardViolation(bool Blocking, string Message);

public static class GeneratorGuard
{
    public const int MaxOperations = 400;
    /// <summary>Her turda en fazla üç öncelikli soru (§8.3).</summary>
    public const int MaxNewQuestions = 3;

    private static readonly IReadOnlyDictionary<GenerationTask, ObjectType[]> Allowed = new Dictionary<GenerationTask, ObjectType[]>
    {
        [GenerationTask.Extract] = [ObjectType.Requirement, ObjectType.Assumption, ObjectType.OpenQuestion, ObjectType.Actor],
        [GenerationTask.Architecture] =
        [
            ObjectType.Assumption, ObjectType.Decision, ObjectType.Component, ObjectType.Relationship, ObjectType.Entity,
            ObjectType.Actor, ObjectType.Flow, ObjectType.Endpoint, ObjectType.TraceLink, ObjectType.OpenQuestion,
        ],
        [GenerationTask.Change] =
        [
            ObjectType.Requirement, ObjectType.Assumption, ObjectType.Decision, ObjectType.Component, ObjectType.Relationship,
            ObjectType.Entity, ObjectType.Actor, ObjectType.Flow, ObjectType.Endpoint, ObjectType.TraceLink, ObjectType.OpenQuestion,
        ],
    };

    private static readonly HashSet<ObjectType> Reviewable =
    [
        ObjectType.Requirement, ObjectType.Assumption, ObjectType.Decision, ObjectType.Component, ObjectType.Relationship,
        ObjectType.Entity, ObjectType.Actor, ObjectType.Flow, ObjectType.Endpoint, ObjectType.TraceLink, ObjectType.OpenQuestion,
    ];

    /// <summary>
    /// Kuralları zorlar ve düzeltilmiş patch ile ihlalleri döndürür. İhlal varsa onarım denemesi tetiklenebilir.
    /// <paramref name="patch"/> kimlik çözümünden SONRA verilmelidir.
    /// </summary>
    public static (ModelPatch Patch, IReadOnlyList<GuardViolation> Violations) Enforce(
        GenerationTask task, ArchitectureModel baseModel, ModelPatch patch)
    {
        var violations = new List<GuardViolation>();
        if (patch.Operations.Count > MaxOperations)
        {
            violations.Add(new GuardViolation(true, $"Çok fazla işlem ({patch.Operations.Count}); en fazla {MaxOperations}."));
            return (patch with { Operations = patch.Operations.Take(MaxOperations).ToList() }, violations);
        }

        var index = new ModelIndex(baseModel);
        var sources = baseModel.Sources.ToDictionary(s => s.Id, s => Normalize(s.Text));
        var ops = new List<PatchOperation>();

        foreach (var op in patch.Operations)
        {
            if (op.Type == ObjectType.Source)
            {
                violations.Add(new GuardViolation(true, $"{op.Id}: kaynak nesnelerine üretici dokunamaz."));
                continue;
            }
            if (!Allowed[task].Contains(op.Type))
            {
                violations.Add(new GuardViolation(true, $"{op.Id}: '{task}' görevinde {op.Type} nesnesi üretilemez."));
                continue;
            }

            if (op.Op == PatchOpKind.Remove)
            {
                if (task != GenerationTask.Change)
                    violations.Add(new GuardViolation(true, $"{op.Id}: '{task}' görevinde silme yapılamaz."));
                else
                    ops.Add(op);
                continue;
            }

            if (op.Value is not { ValueKind: JsonValueKind.Object } value)
            {
                ops.Add(op);
                continue;
            }

            var node = JsonNode.Parse(value.GetRawText())!.AsObject();
            var existing = index.Find(op.Id);

            if (Reviewable.Contains(op.Type))
            {
                if (existing is not null)
                {
                    // Var olan nesnenin kaynağı (origin) üretici tarafından değiştirilemez.
                    var baseNode = JsonNode.Parse(ArcvoraJson.Serialize(existing.Value))!.AsObject();
                    node["origin"] = baseNode["origin"]?.DeepClone();
                    node["reviewStatus"] = baseNode["reviewStatus"]?.DeepClone();
                }
                else
                {
                    // Yeni nesneler her zaman insan incelemesine düşer; 'confirmed' iddiası kabul edilmez.
                    var status = node["reviewStatus"]?.GetValue<string>();
                    node["reviewStatus"] = status == "deferred" ? "deferred" : "proposed";

                    var origin = node["origin"]?.GetValue<string>() ?? "system_proposal";
                    if (op.Type == ObjectType.Requirement && origin == "user_statement")
                    {
                        if (!ExcerptsVerified(node, sources))
                        {
                            origin = "system_proposal";
                            violations.Add(new GuardViolation(false, $"{op.Id}: kullanıcı ifadesi olarak işaretlenen gereksinimin alıntısı kaynak metinde bulunamadı; 'sistem önerisi' yapıldı."));
                        }
                    }
                    else if (origin == "user_statement")
                    {
                        origin = "system_proposal";
                    }
                    node["origin"] = origin;
                }
            }

            ops.Add(op with { Value = JsonSerializer.SerializeToElement(node, ArcvoraJson.Options) });
        }

        ops = CapQuestions(index, ops, violations);
        return ((patch with { Operations = ops }).WithDefaultGroups(), violations);
    }

    /// <summary>
    /// Yeni açık soruları önceliğe göre (etki: kritik → düşük, eşitlikte üretim sırası) en fazla <see cref="MaxNewQuestions"/>
    /// ile sınırlar; çıkarılan sorulara işaret eden izlenebilirlik bağlantıları da çıkarılır (sarkan referans kalmaz).
    /// </summary>
    private static List<PatchOperation> CapQuestions(ModelIndex index, List<PatchOperation> ops, List<GuardViolation> violations)
    {
        var fresh = ops.Select((o, i) => (Op: o, Order: i))
            .Where(x => x.Op is { Op: PatchOpKind.Upsert, Type: ObjectType.OpenQuestion } && !index.Contains(x.Op.Id)).ToList();
        if (fresh.Count <= MaxNewQuestions) return ops;

        static int Rank(PatchOperation o) => o.Value is { ValueKind: JsonValueKind.Object } v
                                             && v.TryGetProperty("impact", out var i) ? i.GetString() switch
        {
            "critical" => 0, "high" => 1, "low" => 3, _ => 2,
        } : 2;

        var drop = fresh.OrderBy(x => Rank(x.Op)).ThenBy(x => x.Order).Skip(MaxNewQuestions).Select(x => x.Op.Id).ToHashSet();
        violations.Add(new GuardViolation(false,
            $"Her turda en fazla {MaxNewQuestions} öncelikli soru: {drop.Count} düşük öncelikli soru çıkarıldı ({string.Join(", ", drop.OrderBy(x => x, StringComparer.Ordinal))})."));

        return ops.Where(o =>
            !(o.Type == ObjectType.OpenQuestion && drop.Contains(o.Id))
            && !(o.Type == ObjectType.TraceLink && o.Value is { } v && Ids.StructuralRefs(v.GetRawText()).Any(drop.Contains))).ToList();
    }

    private static bool ExcerptsVerified(JsonObject req, Dictionary<string, string> sources)
    {
        if (req["sourceRefs"] is not JsonArray refs || refs.Count == 0) return false;
        foreach (var r in refs.OfType<JsonObject>())
        {
            var sid = r["sourceId"]?.GetValue<string>();
            var excerpt = r["excerpt"]?.GetValue<string>();
            if (sid is null || !sources.TryGetValue(sid, out var text)) return false;
            if (string.IsNullOrWhiteSpace(excerpt)) return false;
            if (!text.Contains(Normalize(excerpt), StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string Normalize(string s) =>
        string.Join(' ', s.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
