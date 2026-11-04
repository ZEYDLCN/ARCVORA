using System.Text.Json;
using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;

namespace Arcvora.Core.Ai;

public sealed record DraftGroup
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> ObjectIds { get; init; } = [];
}

public sealed record DraftRemoval
{
    public required ObjectType Type { get; init; }
    public required string Id { get; init; }
}

public sealed record DraftPossibleImpact
{
    public required string ObjectId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// LLM'in araç çağrısıyla döndürdüğü taslağın şekli. Nesne kimlikleri sağlayıcı-geçicidir ("new-1");
/// gerçek kimlikleri <see cref="DraftResolver"/> atar.
/// </summary>
public sealed record ModelDraft
{
    public string Summary { get; init; } = "";
    public IReadOnlyList<DraftGroup> Groups { get; init; } = [];
    public IReadOnlyList<Requirement> Requirements { get; init; } = [];
    public IReadOnlyList<Assumption> Assumptions { get; init; } = [];
    public IReadOnlyList<Decision> Decisions { get; init; } = [];
    public IReadOnlyList<ArchComponent> Components { get; init; } = [];
    public IReadOnlyList<Relationship> Relationships { get; init; } = [];
    public IReadOnlyList<DataEntity> Entities { get; init; } = [];
    public IReadOnlyList<Actor> Actors { get; init; } = [];
    public IReadOnlyList<Flow> Flows { get; init; } = [];
    public IReadOnlyList<ApiEndpoint> Endpoints { get; init; } = [];
    public IReadOnlyList<TraceLink> TraceLinks { get; init; } = [];
    public IReadOnlyList<OpenQuestion> OpenQuestions { get; init; } = [];
    public IReadOnlyList<DraftRemoval> Removals { get; init; } = [];
    public IReadOnlyList<DraftPossibleImpact> PossibleImpacts { get; init; } = [];

    public ModelPatch ToPatch()
    {
        var groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var g in Groups)
            foreach (var id in g.ObjectIds) groupOf.TryAdd(id, g.Id);

        string? G(string id) => groupOf.GetValueOrDefault(id);

        var ops = new List<PatchOperation>();
        ops.AddRange(Requirements.Select(x => ModelPatch.Upsert(ObjectType.Requirement, x, G(x.Id))));
        ops.AddRange(Assumptions.Select(x => ModelPatch.Upsert(ObjectType.Assumption, x, G(x.Id))));
        ops.AddRange(Decisions.Select(x => ModelPatch.Upsert(ObjectType.Decision, x, G(x.Id))));
        ops.AddRange(Components.Select(x => ModelPatch.Upsert(ObjectType.Component, x, G(x.Id))));
        ops.AddRange(Relationships.Select(x => ModelPatch.Upsert(ObjectType.Relationship, x, G(x.Id))));
        ops.AddRange(Entities.Select(x => ModelPatch.Upsert(ObjectType.Entity, x, G(x.Id))));
        ops.AddRange(Actors.Select(x => ModelPatch.Upsert(ObjectType.Actor, x, G(x.Id))));
        ops.AddRange(Flows.Select(x => ModelPatch.Upsert(ObjectType.Flow, x, G(x.Id))));
        ops.AddRange(Endpoints.Select(x => ModelPatch.Upsert(ObjectType.Endpoint, x, G(x.Id))));
        ops.AddRange(TraceLinks.Select(x => ModelPatch.Upsert(ObjectType.TraceLink, x, G(x.Id))));
        ops.AddRange(OpenQuestions.Select(x => ModelPatch.Upsert(ObjectType.OpenQuestion, x, G(x.Id))));
        ops.AddRange(Removals.Select(x => ModelPatch.Remove(x.Type, x.Id, G(x.Id))));

        return new ModelPatch
        {
            Summary = Summary,
            Operations = ops,
            Groups = Groups.Select(g => new PatchGroup { Id = g.Id, Title = g.Title, Description = g.Description }).ToList(),
        };
    }

    public IReadOnlyList<PossibleImpact> ToPossibleImpacts() =>
        PossibleImpacts.Select(p => new PossibleImpact(p.ObjectId, p.Reason)).ToList();

    public static ModelDraft Parse(string json) =>
        JsonSerializer.Deserialize<ModelDraft>(json, ArcvoraJson.Strict) ?? throw new JsonException("Boş taslak.");
}
