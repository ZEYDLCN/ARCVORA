using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Validation;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

public sealed record VersionInfo(Guid Id, int Number, string Summary, string Kind, string ContentHash, Guid CreatedBy,
    DateTimeOffset CreatedAt, Guid? ParentVersionId, Guid? ProposalId);

/// <summary>
/// Değişmez, doğrusal sürüm geçmişi. Aynı numaranın iki kez yazılması benzersiz indeksle engellenir;
/// yarışan iki yazıcıdan biri <c>version_conflict</c> alır ve son yazan diğerinin değişikliğini ezemez.
/// </summary>
public sealed class VersionStore(ArcvoraDbContext db, TimeProvider clock)
{
    public static VersionInfo ToInfo(ModelVersion v) =>
        new(v.Id, v.Number, v.Summary, v.Kind, v.ContentHash, v.CreatedBy, v.CreatedAt, v.ParentVersionId, v.ProposalId);

    public async Task<ModelVersion> GetAsync(Guid projectId, int number, CancellationToken ct) =>
        await db.ModelVersions.AsNoTracking().FirstOrDefaultAsync(v => v.ProjectId == projectId && v.Number == number, ct)
        ?? throw new NotFoundException($"v{number} sürümü bulunamadı.");

    public async Task<ModelVersion> GetByIdAsync(Guid projectId, Guid versionId, CancellationToken ct) =>
        await db.ModelVersions.AsNoTracking().FirstOrDefaultAsync(v => v.ProjectId == projectId && v.Id == versionId, ct)
        ?? throw new NotFoundException("Sürüm bulunamadı.");

    public Task<ModelVersion> GetHeadAsync(Project project, CancellationToken ct) => GetAsync(project.Id, project.HeadNumber, ct);

    public static ArchitectureModel Load(ModelVersion v) => ArcvoraJson.Deserialize<ArchitectureModel>(v.ModelJson);

    /// <summary>İstemci belirli bir sürüme bakarak karar verdiyse, o sürümün hâlâ güncel olduğunu doğrular.</summary>
    public static void EnsureBase(Project project, int? expectedBase)
    {
        if (expectedBase is { } b && b != project.HeadNumber)
            throw new ConflictException("version_conflict",
                $"Model bu arada değişti (istenen temel v{b}, güncel v{project.HeadNumber}). Güncel sürümü yükleyip yeniden deneyin.",
                new { baseVersion = b, headVersion = project.HeadNumber });
    }

    /// <summary>
    /// Yeni sürümü ekler ve proje başlığını ilerletir; çağıranın <paramref name="sameTransaction"/> değişiklikleri de
    /// aynı SaveChanges içinde atomik yazılır. Yeni engelleyici doğrulama hatası varsa yazılmaz.
    /// </summary>
    public async Task<ModelVersion> CommitAsync(Project project, ArchitectureModel? baseModel, ArchitectureModel model,
        Guid actor, string summary, string kind, Guid? proposalId, Action<ModelVersion>? sameTransaction, CancellationToken ct)
    {
        var blocking = baseModel is null
            ? ModelValidator.Validate(model).Blocking.ToList()
            : ModelPatcher.NewBlockingFindings(baseModel, model).ToList();
        if (blocking.Count > 0)
            throw new RequestValidationException("validation_failed", "Değişiklik modelde geçersiz referans veya şema hatası üretiyor.",
                blocking.Select(f => new { f.RuleId, f.ObjectId, f.Message }).ToList());

        Guid? parent = project.HeadNumber == 0
            ? null
            : await db.ModelVersions.Where(v => v.ProjectId == project.Id && v.Number == project.HeadNumber).Select(v => (Guid?)v.Id).FirstOrDefaultAsync(ct);

        var normalized = ModelNormalizer.Normalize(model);
        var version = new ModelVersion
        {
            Id = Guid.NewGuid(), ProjectId = project.Id, Number = project.HeadNumber + 1, ParentVersionId = parent,
            ModelJson = ArcvoraJson.Serialize(normalized), ContentHash = ModelHasher.Hash(normalized),
            Summary = Truncate(summary, 500), Kind = kind, ProposalId = proposalId, CreatedBy = actor, CreatedAt = clock.GetUtcNow(),
        };
        db.ModelVersions.Add(version);
        project.HeadNumber = version.Number;
        sameTransaction?.Invoke(version);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("version_conflict", "Model bu arada değişti. Güncel sürümü yükleyip yeniden deneyin.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("version_conflict", "Model bu arada değişti. Güncel sürümü yükleyip yeniden deneyin.");
        }
        return version;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
