using Arcvora.Core.Model;
using Arcvora.Core.Patching;

namespace Arcvora.Core.Impact;

/// <summary>Etki türleri (§16.1). Graf üzerinde erişilebilir olmak "kesinlikle değişmeli" demek değildir.</summary>
public enum ImpactLevel
{
    /// <summary>Değişen gereksinim veya sözleşme üzerinde açık düzenleme.</summary>
    Direct,
    /// <summary>Doğrudan değişikliğe bağlı model nesnesi.</summary>
    Dependent,
    /// <summary>Semantik incelemeden çıkan, insan kontrolü gereken öneri.</summary>
    Possible,
    /// <summary>Eksik karar veya cevapsız soru nedeniyle henüz belirlenemeyen etki.</summary>
    Uncertain,
}

public enum ChangeKind { Added, Changed, Removed, None }

/// <summary>Mevcut bağlantılarda görünmeyen, LLM/insan incelemesinden gelen olası etki.</summary>
public sealed record PossibleImpact(string ObjectId, string Reason);

public sealed record ImpactItem
{
    public required ObjectType Type { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required ImpactLevel Level { get; init; }
    public required ChangeKind Change { get; init; }
    public required string Reason { get; init; }
    /// <summary>Etkiyi tetikleyen nesne (bağımlı etkiler için).</summary>
    public string? Via { get; init; }
    public string? GroupId { get; init; }
    /// <summary>Bu nesneyi cevap bekleyen sorulara bağlayan açık soru kimlikleri.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = [];
}

public sealed record AreaImpact(string Area, int Direct, int Dependent, int Possible, int Uncertain);

public sealed record ImpactReport
{
    public IReadOnlyList<ImpactItem> Items { get; init; } = [];
    public IReadOnlyList<AreaImpact> Areas { get; init; } = [];
    /// <summary>Bu değişiklikle yeni eklenen veya hâlâ cevap bekleyen sorular.</summary>
    public IReadOnlyList<string> OpenQuestionIds { get; init; } = [];
}

public static class ImpactAnalyzer
{
    private const int MaxDepth = 2;

    public static ImpactReport Analyze(ArchitectureModel baseModel, ArchitectureModel resultModel,
        ModelPatch? patch = null, IEnumerable<PossibleImpact>? possible = null)
    {
        var baseIndex = new ModelIndex(baseModel);
        var resultIndex = new ModelIndex(resultModel);
        var groupOf = (patch?.WithDefaultGroups().Operations ?? [])
            .GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First().GroupId);

        var items = new Dictionary<string, ImpactItem>(StringComparer.Ordinal);
        var diff = Diff(baseIndex, resultIndex);

        // 1) Doğrudan etkiler
        foreach (var c in diff)
        {
            var after = c.After ?? c.Before!;
            items[after.Id] = new ImpactItem
            {
                Type = after.Type, Id = after.Id, Title = after.Title, Level = ImpactLevel.Direct, Change = c.Kind,
                Reason = c.Kind switch
                {
                    ChangeKind.Added => "Yeni nesne ekleniyor.",
                    ChangeKind.Removed => "Nesne siliniyor.",
                    _ => "Nesne değiştiriliyor.",
                },
                GroupId = groupOf.GetValueOrDefault(after.Id),
            };
        }

        // 2) Bağımlı etkiler: değişen/silinen nesnelerden yönlü bağımlılık kenarları boyunca
        var edges = BuildEdges(baseModel).Concat(BuildEdges(resultModel)).Distinct().ToLookup(e => e.From);
        var seeds = diff.Where(c => c.Kind is ChangeKind.Changed or ChangeKind.Removed).Select(c => (c.After ?? c.Before)!.Id);
        var queue = new Queue<(string Id, int Depth, string Seed)>(seeds.Select(s => (s, 0, s)));
        var seen = new HashSet<string>(items.Keys, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var (id, depth, seed) = queue.Dequeue();
            if (!visited.Add(id + "#" + depth)) continue;
            if (depth >= MaxDepth) continue;
            var node = resultIndex.Find(id) ?? baseIndex.Find(id);
            // Merkez düğümler (bileşen/aktör) kök değilse genişletilmez: tek bir bileşen üzerinden tüm graf işaretlenmez.
            if (depth >= 1 && node is { Type: ObjectType.Component or ObjectType.Actor or ObjectType.Source }) continue;

            foreach (var e in edges[id])
            {
                if (seen.Contains(e.To)) continue;
                var target = resultIndex.Find(e.To) ?? baseIndex.Find(e.To);
                if (target is null) continue;
                seen.Add(e.To);
                items[e.To] = new ImpactItem
                {
                    Type = target.Type, Id = target.Id, Title = target.Title, Level = ImpactLevel.Dependent, Change = ChangeKind.None,
                    Reason = $"{e.Label}: {id} {TitleOf(id, resultIndex, baseIndex)}", Via = id,
                    GroupId = groupOf.GetValueOrDefault(seed),
                };
                queue.Enqueue((e.To, depth + 1, seed));
            }
        }

        // Yeni eklenen bağlantılar: nesne yeni bir bileşene bağlanıyorsa o bileşen etkilenir.
        foreach (var c in diff.Where(c => c.Kind is ChangeKind.Added or ChangeKind.Changed && c.After is not null))
        {
            if (c.After!.Type == ObjectType.TraceLink) continue;
            var oldRefs = c.Before is null ? [] : RefsOf(c.Before).ToHashSet();
            foreach (var r in RefsOf(c.After).Where(r => !oldRefs.Contains(r) && r != c.After.Id))
            {
                if (seen.Contains(r)) continue;
                if (baseIndex.Find(r) is not { Type: ObjectType.Component } comp) continue;
                seen.Add(r);
                items[r] = new ImpactItem
                {
                    Type = comp.Type, Id = comp.Id, Title = comp.Title, Level = ImpactLevel.Dependent, Change = ChangeKind.None,
                    Reason = $"Yeni bağlantı: {c.After.Id} {c.After.Title} bu bileşeni kullanıyor.", Via = c.After.Id,
                    GroupId = groupOf.GetValueOrDefault(c.After.Id),
                };
            }
        }

        // 3) Olası etkiler (insan kontrolü gerekir)
        foreach (var p in possible ?? [])
        {
            if (seen.Contains(p.ObjectId)) continue;
            var o = resultIndex.Find(p.ObjectId) ?? baseIndex.Find(p.ObjectId);
            if (o is null) continue;
            seen.Add(p.ObjectId);
            items[p.ObjectId] = new ImpactItem
            {
                Type = o.Type, Id = o.Id, Title = o.Title, Level = ImpactLevel.Possible, Change = ChangeKind.None,
                Reason = p.Reason,
            };
        }

        // 4) Belirsiz etkiler: cevap bekleyen sorulara bağlı nesneler
        var openQuestions = resultModel.OpenQuestions
            .Where(q => q.Status is QuestionStatus.Open or QuestionStatus.Deferred).Select(q => q.Id).ToHashSet();
        var blockersOf = resultModel.TraceLinks.Where(t => t.Kind == TraceKind.BlockedBy && openQuestions.Contains(t.ToId))
            .GroupBy(t => t.FromId).ToDictionary(g => g.Key, g => g.Select(t => t.ToId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList());
        var touchedQuestions = diff.Where(c => (c.After ?? c.Before)!.Type == ObjectType.OpenQuestion)
            .Select(c => (c.After ?? c.Before)!.Id).ToHashSet();

        foreach (var (objId, qs) in blockersOf)
        {
            if (items.TryGetValue(objId, out var existing))
            {
                items[objId] = existing with { Blockers = qs };
            }
            else if (qs.Any(touchedQuestions.Contains) && resultIndex.Find(objId) is { } o)
            {
                items[objId] = new ImpactItem
                {
                    Type = o.Type, Id = o.Id, Title = o.Title, Level = ImpactLevel.Uncertain, Change = ChangeKind.None,
                    Reason = $"Etkisi cevap bekleyen soruya bağlı: {string.Join(", ", qs)}.", Blockers = qs,
                };
            }
        }

        var ordered = items.Values
            .OrderBy(i => i.Level).ThenBy(i => Area(i.Type)).ThenBy(i => i.Id, StringComparer.Ordinal).ToList();

        var areas = ordered.GroupBy(i => Area(i.Type)).OrderBy(g => AreaOrder(g.Key)).Select(g => new AreaImpact(
            g.Key,
            g.Count(i => i.Level == ImpactLevel.Direct), g.Count(i => i.Level == ImpactLevel.Dependent),
            g.Count(i => i.Level == ImpactLevel.Possible), g.Count(i => i.Level == ImpactLevel.Uncertain))).ToList();

        var openIds = items.Values.SelectMany(i => i.Blockers)
            .Concat(touchedQuestions.Where(openQuestions.Contains))
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        return new ImpactReport { Items = ordered, Areas = areas, OpenQuestionIds = openIds };
    }

    public static string Area(ObjectType t) => t switch
    {
        ObjectType.Requirement or ObjectType.Assumption => "Gereksinimler",
        ObjectType.Decision => "Kararlar",
        ObjectType.Component or ObjectType.Relationship => "Bileşenler",
        ObjectType.Entity => "Veri modeli",
        ObjectType.Endpoint => "API",
        ObjectType.Flow or ObjectType.Actor => "Akışlar",
        ObjectType.OpenQuestion => "Açık sorular",
        ObjectType.TraceLink => "İzlenebilirlik",
        _ => "Kaynaklar",
    };

    private static int AreaOrder(string a) => a switch
    {
        "Gereksinimler" => 0, "Kararlar" => 1, "Bileşenler" => 2, "Veri modeli" => 3, "API" => 4,
        "Akışlar" => 5, "Açık sorular" => 6, "İzlenebilirlik" => 7, _ => 8,
    };

    // ----------------------------------------------------------------- fark

    private sealed record Change(ChangeKind Kind, IndexedObject? Before, IndexedObject? After);

    private static List<Change> Diff(ModelIndex before, ModelIndex after)
    {
        var changes = new List<Change>();
        foreach (var a in after.All)
        {
            if (before.Find(a.Id) is not { } b) changes.Add(new Change(ChangeKind.Added, null, a));
            else if (ArcvoraJson.Serialize(b.Value) != ArcvoraJson.Serialize(a.Value)) changes.Add(new Change(ChangeKind.Changed, b, a));
        }
        foreach (var b in before.All.Where(b => !after.Contains(b.Id)))
            changes.Add(new Change(ChangeKind.Removed, b, null));
        return changes;
    }

    private static IEnumerable<string> RefsOf(IndexedObject o) =>
        Ids.StructuralRefs(ArcvoraJson.Serialize(o.Value)).Where(r => r != o.Id).Distinct();

    private static string TitleOf(string id, ModelIndex a, ModelIndex b) => (a.Find(id) ?? b.Find(id))?.Title ?? "";

    // ---------------------------------------------------------------- kenarlar

    private readonly record struct Edge(string From, string To, string Label);

    /// <summary>"From değişirse To etkilenebilir" yönlü kenarları.</summary>
    private static IEnumerable<Edge> BuildEdges(ArchitectureModel m)
    {
        foreach (var t in m.TraceLinks)
        {
            switch (t.Kind)
            {
                case TraceKind.Addresses: yield return new Edge(t.ToId, t.FromId, "Bu gereksinime dayanan karar"); break;
                case TraceKind.Implements: yield return new Edge(t.ToId, t.FromId, "Bu kararı uygulayan nesne"); break;
                case TraceKind.DependsOn: yield return new Edge(t.ToId, t.FromId, "Bu karara bağlı karar"); break;
                case TraceKind.Assumes: yield return new Edge(t.ToId, t.FromId, "Bu varsayıma dayanan karar"); break;
                case TraceKind.BlockedBy: yield return new Edge(t.ToId, t.FromId, "Bu soruya bağlı nesne"); break;
            }
        }
        foreach (var r in m.Relationships)
        {
            yield return new Edge(r.SourceId, r.Id, "Bileşenin giden bağlantısı");
            yield return new Edge(r.TargetId, r.Id, "Bileşenin gelen bağlantısı");
        }
        foreach (var e in m.Entities)
        {
            if (e.OwnerComponentId is not null) yield return new Edge(e.OwnerComponentId, e.Id, "Bileşenin sahip olduğu veri");
            foreach (var rel in e.Relations) yield return new Edge(rel.TargetEntityId, e.Id, "Bu varlığa ilişkili varlık");
        }
        foreach (var f in m.Flows)
            foreach (var p in f.Participants) yield return new Edge(p, f.Id, "Katılımcının yer aldığı akış");
        foreach (var ep in m.Endpoints)
            if (ep.ComponentId is not null) yield return new Edge(ep.ComponentId, ep.Id, "Bileşenin sunduğu endpoint");
    }
}
