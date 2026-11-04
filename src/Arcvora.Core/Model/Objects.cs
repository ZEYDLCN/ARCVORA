namespace Arcvora.Core.Model;

public interface IModelObject
{
    string Id { get; }
}

/// <summary>İnsan incelemesinden geçen nesneler.</summary>
public interface IReviewable : IModelObject
{
    Origin Origin { get; }
    ReviewStatus ReviewStatus { get; }
}

public sealed record ProjectInfo
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string TechProfileId { get; init; } = "aspnetcore";
}

public sealed record SourceRef
{
    public required string SourceId { get; init; }
    public string? Excerpt { get; init; }
}

public sealed record Source : IModelObject
{
    public required string Id { get; init; }
    public string Title { get; init; } = "";
    public required string Text { get; init; }
    /// <summary>"text" veya "markdown".</summary>
    public string Format { get; init; } = "text";
}

public sealed record AcceptanceCriterion
{
    public required string Id { get; init; }
    public required string Text { get; init; }
}

public sealed record Requirement : IReviewable
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public RequirementKind Kind { get; init; } = RequirementKind.Functional;
    public Priority Priority { get; init; } = Priority.Medium;
    public Origin Origin { get; init; } = Origin.UserStatement;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
    public IReadOnlyList<SourceRef> SourceRefs { get; init; } = [];
    public IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria { get; init; } = [];
}

public sealed record Assumption : IReviewable
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public string Rationale { get; init; } = "";
    public AssumptionStatus Status { get; init; } = AssumptionStatus.Unverified;
    public Origin Origin { get; init; } = Origin.Assumption;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record Alternative
{
    public required string Name { get; init; }
    public string Tradeoff { get; init; } = "";
}

public sealed record Decision : IReviewable
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Choice { get; init; } = "";
    public IReadOnlyList<Alternative> Alternatives { get; init; } = [];
    public string Rationale { get; init; } = "";
    public string Consequences { get; init; } = "";
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record ArchComponent : IReviewable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public ComponentKind Kind { get; init; } = ComponentKind.Module;
    public string Responsibility { get; init; } = "";
    public string? Technology { get; init; }
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record Relationship : IReviewable
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string TargetId { get; init; }
    public CommunicationKind Communication { get; init; } = CommunicationKind.Sync;
    public string Description { get; init; } = "";
    public string? FailureBehavior { get; init; }
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record EntityField
{
    public required string Name { get; init; }
    public string Type { get; init; } = "string";
    public bool IsKey { get; init; }
    public bool IsNullable { get; init; }
    public string? Notes { get; init; }
}

public sealed record EntityRelation
{
    public required string TargetEntityId { get; init; }
    public Cardinality Cardinality { get; init; } = Cardinality.OneToMany;
    public string Label { get; init; } = "";
}

public sealed record DataEntity : IReviewable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Verinin sahibi olan bileşen (§3: "Bir verinin sahibi hangi modüldür?").</summary>
    public string? OwnerComponentId { get; init; }
    public IReadOnlyList<EntityField> Fields { get; init; } = [];
    public IReadOnlyList<EntityRelation> Relations { get; init; } = [];
    public IReadOnlyList<string> Constraints { get; init; } = [];
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record Actor : IReviewable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public Origin Origin { get; init; } = Origin.UserStatement;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record FlowStep
{
    /// <summary>Katılımcı kimliği: bir ACT- veya CMP- kimliği.</summary>
    public required string From { get; init; }
    public required string To { get; init; }
    public required string Message { get; init; }
    public StepKind Kind { get; init; } = StepKind.Call;
    /// <summary>Aynı AltId taşıyan ardışık adımlar tek bir alt/else bloğu oluşturur.</summary>
    public string? AltId { get; init; }
    public string? Condition { get; init; }
}

public sealed record Flow : IReviewable
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = "";
    public IReadOnlyList<string> Participants { get; init; } = [];
    public IReadOnlyList<FlowStep> Steps { get; init; } = [];
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record ApiEndpoint : IReviewable
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public string Summary { get; init; } = "";
    public string? ComponentId { get; init; }
    public string? Authorization { get; init; }
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record TraceLink : IReviewable
{
    public required string Id { get; init; }
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public TraceKind Kind { get; init; }
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

public sealed record OpenQuestion : IReviewable
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    /// <summary>Cevabın mimari kararı değiştirme ihtimali (§8.3 önceliği).</summary>
    public Priority Impact { get; init; } = Priority.Medium;
    public QuestionStatus Status { get; init; } = QuestionStatus.Open;
    public string? Answer { get; init; }
    public Origin Origin { get; init; } = Origin.SystemProposal;
    public ReviewStatus ReviewStatus { get; init; } = ReviewStatus.Proposed;
}

/// <summary>
/// Sürümlenebilir ortak mimari model. Diyagramlar bu modelin türetilmiş görünümleridir (§14).
/// </summary>
public sealed record ArchitectureModel
{
    public const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
    public ProjectInfo Project { get; init; } = new();
    public IReadOnlyList<Source> Sources { get; init; } = [];
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
}
