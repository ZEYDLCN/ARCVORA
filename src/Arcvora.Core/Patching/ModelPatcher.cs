using System.Text.Json;
using Arcvora.Core.Model;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Patching;

public sealed record PatchApplyResult(ArchitectureModel? Model, IReadOnlyList<string> Errors)
{
    public bool Ok => Model is not null && Errors.Count == 0;
}

public static class ModelPatcher
{
    /// <summary>
    /// Patch'i modele uygular; girdi modeli değiştirmez. Kimlik çözümü önceden yapılmış olmalıdır.
    /// <paramref name="selectedGroups"/> verilirse yalnızca o grupların işlemleri uygulanır.
    /// <paramref name="confirmApplied"/> true ise uygulanan, henüz 'proposed' olan nesneler 'confirmed' olur;
    /// <c>origin</c> değişmez (kaynak ile onay ayrı alanlardır, §14.2).
    /// </summary>
    public static PatchApplyResult Apply(ArchitectureModel model, ModelPatch patch,
        IReadOnlySet<string>? selectedGroups = null, bool confirmApplied = false)
    {
        var errors = new List<string>();
        var current = model;

        foreach (var op in patch.WithDefaultGroups().Operations)
        {
            if (selectedGroups is not null && !selectedGroups.Contains(op.GroupId!)) continue;

            try
            {
                current = op.Op switch
                {
                    PatchOpKind.Upsert => ApplyUpsert(current, op, confirmApplied, errors),
                    PatchOpKind.Remove => ApplyRemove(current, op, errors),
                    _ => current,
                };
            }
            catch (JsonException ex)
            {
                errors.Add($"{op.Id}: nesne çözümlenemedi ({ex.Message}).");
            }
        }

        return new PatchApplyResult(errors.Count == 0 ? current : null, errors);
    }

    private static ArchitectureModel ApplyUpsert(ArchitectureModel m, PatchOperation op, bool confirm, List<string> errors)
    {
        if (op.Value is not { ValueKind: JsonValueKind.Object } value)
        {
            errors.Add($"{op.Id}: upsert işlemi nesne değeri gerektirir.");
            return m;
        }

        T Read<T>() where T : IModelObject
        {
            var obj = value.Deserialize<T>(ArcvoraJson.Strict)
                      ?? throw new JsonException("Boş nesne.");
            if (obj.Id != op.Id) throw new JsonException($"Nesne kimliği '{obj.Id}' işlem kimliğiyle '{op.Id}' eşleşmiyor.");
            return obj;
        }

        return op.Type switch
        {
            ObjectType.Source => m with { Sources = Upsert(m.Sources, Read<Source>()) },
            ObjectType.Requirement => m with { Requirements = Upsert(m.Requirements, Confirm(Read<Requirement>(), confirm)) },
            ObjectType.Assumption => m with { Assumptions = Upsert(m.Assumptions, Confirm(Read<Assumption>(), confirm)) },
            ObjectType.Decision => m with { Decisions = Upsert(m.Decisions, Confirm(Read<Decision>(), confirm)) },
            ObjectType.Component => m with { Components = Upsert(m.Components, Confirm(Read<ArchComponent>(), confirm)) },
            ObjectType.Relationship => m with { Relationships = Upsert(m.Relationships, Confirm(Read<Relationship>(), confirm)) },
            ObjectType.Entity => m with { Entities = Upsert(m.Entities, Confirm(Read<DataEntity>(), confirm)) },
            ObjectType.Actor => m with { Actors = Upsert(m.Actors, Confirm(Read<Actor>(), confirm)) },
            ObjectType.Flow => m with { Flows = Upsert(m.Flows, Confirm(Read<Flow>(), confirm)) },
            ObjectType.Endpoint => m with { Endpoints = Upsert(m.Endpoints, Confirm(Read<ApiEndpoint>(), confirm)) },
            ObjectType.TraceLink => m with { TraceLinks = Upsert(m.TraceLinks, Confirm(Read<TraceLink>(), confirm)) },
            ObjectType.OpenQuestion => m with { OpenQuestions = Upsert(m.OpenQuestions, Confirm(Read<OpenQuestion>(), confirm)) },
            _ => m,
        };
    }

    private static ArchitectureModel ApplyRemove(ArchitectureModel m, PatchOperation op, List<string> errors)
    {
        var before = new ModelIndex(m);
        if (before.Find(op.Id) is not { } found || found.Type != op.Type)
        {
            errors.Add($"{op.Id}: silinecek {op.Type} modelde yok.");
            return m;
        }

        return op.Type switch
        {
            ObjectType.Source => m with { Sources = Without(m.Sources, op.Id) },
            ObjectType.Requirement => m with { Requirements = Without(m.Requirements, op.Id) },
            ObjectType.Assumption => m with { Assumptions = Without(m.Assumptions, op.Id) },
            ObjectType.Decision => m with { Decisions = Without(m.Decisions, op.Id) },
            ObjectType.Component => m with { Components = Without(m.Components, op.Id) },
            ObjectType.Relationship => m with { Relationships = Without(m.Relationships, op.Id) },
            ObjectType.Entity => m with { Entities = Without(m.Entities, op.Id) },
            ObjectType.Actor => m with { Actors = Without(m.Actors, op.Id) },
            ObjectType.Flow => m with { Flows = Without(m.Flows, op.Id) },
            ObjectType.Endpoint => m with { Endpoints = Without(m.Endpoints, op.Id) },
            ObjectType.TraceLink => m with { TraceLinks = Without(m.TraceLinks, op.Id) },
            ObjectType.OpenQuestion => m with { OpenQuestions = Without(m.OpenQuestions, op.Id) },
            _ => m,
        };
    }

    private static IReadOnlyList<T> Upsert<T>(IReadOnlyList<T> list, T item) where T : IModelObject
    {
        var result = list.ToList();
        var i = result.FindIndex(x => x.Id == item.Id);
        if (i >= 0) result[i] = item; else result.Add(item);
        return result;
    }

    private static IReadOnlyList<T> Without<T>(IReadOnlyList<T> list, string id) where T : IModelObject =>
        list.Where(x => x.Id != id).ToList();

    private static T Confirm<T>(T item, bool confirm) where T : IModelObject
    {
        if (!confirm) return item;
        return item switch
        {
            Requirement { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            Assumption { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            Decision { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            ArchComponent { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            Relationship { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            DataEntity { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            Actor { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            Flow { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            ApiEndpoint { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            TraceLink { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            OpenQuestion { ReviewStatus: ReviewStatus.Proposed } x => (T)(object)(x with { ReviewStatus = ReviewStatus.Confirmed }),
            _ => item,
        };
    }

    /// <summary>
    /// Patch'in temel modele uygulanmış hâlini doğrular ve YALNIZCA patch ile ortaya çıkan (temel modelde
    /// bulunmayan) engelleyici hataları döndürür. Önceden var olan sorunlar yeni değişikliği engellemez.
    /// </summary>
    public static IReadOnlyList<ValidationFinding> NewBlockingFindings(ArchitectureModel baseModel, ArchitectureModel result)
    {
        var before = ModelValidator.Validate(baseModel).Blocking.Select(f => f.Key).ToHashSet();
        return ModelValidator.Validate(result).Blocking.Where(f => !before.Contains(f.Key)).ToList();
    }
}
