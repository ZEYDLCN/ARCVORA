using Arcvora.Core.Model;

namespace Arcvora.Core.Patching;

public sealed record SelectionCheck(IReadOnlySet<string> Selected, IReadOnlyList<string> Errors)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Kısmi kabul tutarlılığı (§16.2): yalnızca bağımlılıkları tutarlı kalan değişiklik grupları seçilebilir.
/// "Yeni bileşeni reddedip ona işaret eden bağlantıyı kabul etmek engellenir."
/// </summary>
public static class GroupDependencies
{
    /// <summary>group → bağımlı olduğu gruplar.</summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Compute(ArchitectureModel baseModel, ModelPatch patch)
    {
        var p = patch.WithDefaultGroups();
        var deps = p.Groups.ToDictionary(g => g.Id, _ => (ISet<string>)new HashSet<string>(StringComparer.Ordinal));

        var baseIndex = new ModelIndex(baseModel);

        // Her grubun yarattığı yeni kimlikler
        var createdBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var op in p.Operations.Where(o => o.Op == PatchOpKind.Upsert && !baseIndex.Contains(o.Id)))
            createdBy.TryAdd(op.Id, op.GroupId!);

        // Değişen/silinen mevcut nesnelerin sahibi grup
        var touchedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var op in p.Operations.Where(o => baseIndex.Contains(o.Id)))
            touchedBy.TryAdd(op.Id, op.GroupId!);

        // 1) Yeni nesneye başvuran grup, onu yaratan gruba bağlıdır
        foreach (var op in p.Operations.Where(o => o.Op == PatchOpKind.Upsert && o.Value is not null))
        {
            foreach (var refId in Ids.StructuralRefs(op.Value!.Value.GetRawText()).Where(r => r != op.Id).Distinct())
            {
                if (createdBy.TryGetValue(refId, out var owner) && owner != op.GroupId)
                    deps[op.GroupId!].Add(owner);
            }
        }

        // 2) Bir nesneyi silen grup, o nesneye başvuran mevcut nesneleri değiştiren/silen gruplara bağlıdır
        var baseRefs = BaseReferences(baseModel);
        foreach (var rm in p.Operations.Where(o => o.Op == PatchOpKind.Remove))
        {
            foreach (var (holder, refs) in baseRefs)
            {
                if (!refs.Contains(rm.Id)) continue;
                if (touchedBy.TryGetValue(holder, out var holderGroup) && holderGroup != rm.GroupId)
                    deps[rm.GroupId!].Add(holderGroup);
            }
        }

        return deps.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value);
    }

    /// <summary>Seçilen grupların bağımlılık altında kapalı olup olmadığını ve kimliklerin geçerliliğini denetler.</summary>
    public static SelectionCheck CheckSelection(ArchitectureModel baseModel, ModelPatch patch, IEnumerable<string>? requested)
    {
        var p = patch.WithDefaultGroups();
        var all = p.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var selected = requested is null ? all : requested.ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();

        foreach (var unknown in selected.Where(s => !all.Contains(s)))
            errors.Add($"Bilinmeyen değişiklik grubu '{unknown}'.");
        if (selected.Count == 0) errors.Add("En az bir değişiklik grubu seçilmeli.");

        var deps = Compute(baseModel, p);
        var titles = p.Groups.ToDictionary(g => g.Id, g => g.Title);
        foreach (var g in selected.Where(all.Contains))
        foreach (var d in deps[g].Where(d => !selected.Contains(d)))
            errors.Add($"'{titles[g]}' değişikliği '{titles[d]}' değişikliğine bağlı; biri olmadan diğeri kabul edilemez.");

        return new SelectionCheck(selected, errors);
    }

    private static Dictionary<string, HashSet<string>> BaseReferences(ArchitectureModel m)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var o in new ModelIndex(m).All)
        {
            var json = ArcvoraJson.Serialize(o.Value);
            result[o.Id] = Ids.StructuralRefs(json).Where(r => r != o.Id).ToHashSet(StringComparer.Ordinal);
        }
        return result;
    }
}
