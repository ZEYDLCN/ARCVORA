using System.Text.Json;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcvora.Infrastructure.Jobs;

/// <summary>Uzun süren işlemler için dayanıklı iş kaydı (202 Accepted + iş kimliği).</summary>
public sealed class JobService(ArcvoraDbContext db, ProjectAccessService access, VersionStore versions,
    IOptions<AiOptions> ai, IOptions<JobOptions> jobs, TimeProvider clock)
{
    public const int MaxRequestTextLength = 8000;

    public async Task<EnqueueResult> EnqueueAsync(Guid userId, Guid projectId, string kind, string? requestText,
        string? idempotencyKey, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Edit, track: false, ct);

        if (idempotencyKey is { Length: > 100 }) throw new BadRequestException("Idempotency-Key en fazla 100 karakter olabilir.");
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.ProjectId == projectId && j.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) return new EnqueueResult(ToDto(existing), false);
        }

        var head = await versions.GetHeadAsync(ctx.Project, ct);
        var model = VersionStore.Load(head);
        string input = "{}";

        switch (kind)
        {
            case JobKinds.Analysis:
                var analyzed = model.Requirements.SelectMany(r => r.SourceRefs).Select(s => s.SourceId).ToHashSet();
                if (model.Sources.All(s => analyzed.Contains(s.Id)))
                    throw new RequestValidationException("no_pending_sources", "Analiz edilecek yeni kaynak yok. Önce bir kaynak metni ekleyin.");
                break;
            case JobKinds.Architecture:
                if (!model.Requirements.Any(r => r.ReviewStatus != ReviewStatus.Rejected))
                    throw new RequestValidationException("no_requirements", "Mimari taslak için en az bir gereksinim gerekli. Önce kaynak analizi yapın veya gereksinim ekleyin.");
                break;
            case JobKinds.Change:
                var text = (requestText ?? "").Trim();
                if (text.Length < 3) throw new RequestValidationException("empty_request", "Değişiklik talebi boş olamaz.");
                if (text.Length > MaxRequestTextLength) throw new RequestValidationException("request_too_long", $"Talep en fazla {MaxRequestTextLength} karakter olabilir.");
                input = JsonSerializer.Serialize(new { requestText = text }, ArcvoraJson.Options);
                break;
            default:
                throw new BadRequestException("Bilinmeyen iş türü.");
        }

        var active = await db.Jobs.CountAsync(j => j.ProjectId == projectId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running), ct);
        if (active >= jobs.Value.MaxActivePerProject)
            throw new ConflictException("too_many_jobs", "Bu projede çok fazla iş sürüyor. Birinin bitmesini bekleyin veya iptal edin.");

        var now = clock.GetUtcNow();
        var job = new Job
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Kind = kind, Status = JobStatus.Queued, InputJson = input,
            MaxAttempts = jobs.Value.MaxAttempts, TokenBudget = ai.Value.MaxTokensPerJob, IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            CreatedBy = userId, CreatedAt = now, UpdatedAt = now,
        };
        db.Jobs.Add(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var existing = await db.Jobs.AsNoTracking().FirstAsync(j => j.ProjectId == projectId && j.IdempotencyKey == idempotencyKey, ct);
            return new EnqueueResult(ToDto(existing), false);
        }
        return new EnqueueResult(ToDto(job), true);
    }

    public async Task<JobDto> GetAsync(Guid userId, Guid jobId, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new NotFoundException("İş bulunamadı.");
        // Erişim, işin ait olduğu projeden doğrulanır; başka çalışma alanının işi 404 döner.
        await access.RequireAsync(userId, job.ProjectId, Permission.Read, track: false, ct);
        return ToDto(job);
    }

    public async Task<IReadOnlyList<JobDto>> ListAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var rows = await db.Jobs.AsNoTracking().Where(j => j.ProjectId == projectId).OrderByDescending(j => j.CreatedAt).Take(30).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<JobDto> CancelAsync(Guid userId, Guid jobId, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new NotFoundException("İş bulunamadı.");
        await access.RequireAsync(userId, job.ProjectId, Permission.Edit, track: false, ct);

        switch (job.Status)
        {
            case JobStatus.Queued:
                job.Status = JobStatus.Cancelled;
                job.Stage = null;
                break;
            case JobStatus.Running:
                // Sağlayıcının yürüyen çağrısı durdurulamayabilir; sonuç iptal sonrası projeye uygulanmaz.
                job.CancelRequested = true;
                break;
            default:
                return ToDto(job);
        }
        job.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return ToDto(job);
    }

    public static JobDto ToDto(Job j) => new(j.Id, j.ProjectId, j.Kind, j.Status.ToString().ToLowerInvariant(), j.Stage, j.Attempts, j.MaxAttempts,
        j.ErrorCode, j.Error, j.ProblemsJson is null ? [] : JsonSerializer.Deserialize<List<string>>(j.ProblemsJson) ?? [],
        j.ProposalId, j.TokensUsed, j.TokenBudget, j.CancelRequested, j.CreatedAt, j.UpdatedAt);
}
