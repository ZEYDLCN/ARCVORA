namespace Arcvora.Core.Model;

public sealed record IndexedObject(ObjectType Type, string Id, string Title, object Value);

/// <summary>Modeldeki tüm nesnelere kimlikle erişim.</summary>
public sealed class ModelIndex
{
    private readonly Dictionary<string, IndexedObject> _byId = new(StringComparer.Ordinal);

    public IReadOnlyList<IndexedObject> All { get; }
    /// <summary>Birden fazla nesnede kullanılan kimlikler (şema hatası).</summary>
    public IReadOnlyList<string> Duplicates { get; }

    public ModelIndex(ArchitectureModel m)
    {
        var all = new List<IndexedObject>();
        var dups = new List<string>();

        void Add(ObjectType t, string id, string title, object v)
        {
            var o = new IndexedObject(t, id, title, v);
            all.Add(o);
            if (!_byId.TryAdd(id, o)) dups.Add(id);
        }

        foreach (var x in m.Sources) Add(ObjectType.Source, x.Id, string.IsNullOrWhiteSpace(x.Title) ? Truncate(x.Text) : x.Title, x);
        foreach (var x in m.Requirements) Add(ObjectType.Requirement, x.Id, Truncate(x.Text), x);
        foreach (var x in m.Assumptions) Add(ObjectType.Assumption, x.Id, Truncate(x.Text), x);
        foreach (var x in m.Decisions) Add(ObjectType.Decision, x.Id, x.Title, x);
        foreach (var x in m.Components) Add(ObjectType.Component, x.Id, x.Name, x);
        foreach (var x in m.Relationships) Add(ObjectType.Relationship, x.Id, $"{x.SourceId} → {x.TargetId}", x);
        foreach (var x in m.Entities) Add(ObjectType.Entity, x.Id, x.Name, x);
        foreach (var x in m.Actors) Add(ObjectType.Actor, x.Id, x.Name, x);
        foreach (var x in m.Flows) Add(ObjectType.Flow, x.Id, x.Name, x);
        foreach (var x in m.Endpoints) Add(ObjectType.Endpoint, x.Id, $"{x.Method} {x.Path}", x);
        foreach (var x in m.TraceLinks) Add(ObjectType.TraceLink, x.Id, $"{x.FromId} {x.Kind} {x.ToId}", x);
        foreach (var x in m.OpenQuestions) Add(ObjectType.OpenQuestion, x.Id, Truncate(x.Text), x);

        All = all;
        Duplicates = dups;
    }

    public bool Contains(string? id) => id is not null && _byId.ContainsKey(id);

    public IndexedObject? Find(string? id) => id is not null && _byId.TryGetValue(id, out var o) ? o : null;

    public ObjectType? TypeOf(string? id) => Find(id)?.Type;

    public string TitleOf(string id) => Find(id)?.Title ?? id;

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..77] + "...";
}

public static class ModelNormalizer
{
    /// <summary>Listeleri kimliğe göre sıralar; aynı içerik aynı hash'i üretir. Akış adımları sırasını korur.</summary>
    public static ArchitectureModel Normalize(ArchitectureModel m) => m with
    {
        Sources = [.. m.Sources.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Requirements = [.. m.Requirements.OrderBy(x => x.Id, StringComparer.Ordinal)
            .Select(r => r with
            {
                AcceptanceCriteria = [.. r.AcceptanceCriteria.OrderBy(a => a.Id, StringComparer.Ordinal)],
            })],
        Assumptions = [.. m.Assumptions.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Decisions = [.. m.Decisions.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Components = [.. m.Components.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Relationships = [.. m.Relationships.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Entities = [.. m.Entities.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Actors = [.. m.Actors.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Flows = [.. m.Flows.OrderBy(x => x.Id, StringComparer.Ordinal)],
        Endpoints = [.. m.Endpoints.OrderBy(x => x.Id, StringComparer.Ordinal)],
        TraceLinks = [.. m.TraceLinks.OrderBy(x => x.Id, StringComparer.Ordinal)],
        OpenQuestions = [.. m.OpenQuestions.OrderBy(x => x.Id, StringComparer.Ordinal)],
    };
}
