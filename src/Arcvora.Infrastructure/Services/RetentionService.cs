using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Files;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

public sealed record RetentionDto(int? ClosedRequestMonths, bool AutoPurge);
public sealed record UpdateRetentionRequest(int? ClosedRequestMonths, bool? Clear, bool? AutoPurge);
public sealed record RetentionPreviewDto(int Eligible, int SkippedAccounting, int? Months);
public sealed record PurgeRequest(bool? Confirm);
public sealed record PurgeResultDto(int Purged, int Skipped);

/// <summary>
/// Saklama süresi ve silme (KVKK). Silinen talebin görevleri, zaman çizelgesi, muhasebe kaydı ve ekleri (dosya baytları dahil) birlikte gider.
/// Açık talepler ve muhasebe kaydı henüz işlenmemiş (Beklemede/Hata) talepler asla silinmez. Her silme denetim kaydına yazılır.
/// </summary>
public sealed class RetentionService(ArcvoraDbContext db, AppAccessService access, IFileStore store, AuditLog audit, TimeProvider clock)
{
    public const int MinMonths = 6;
    public const int MaxMonths = 240;
    private const int BatchSize = 200;
    private const int MaxPerRun = 1000;

    // =================================================================== ayarlar

    public async Task<RetentionDto> GetAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var s = await db.RetentionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        return new RetentionDto(s?.ClosedRequestMonths, s?.AutoPurge ?? false);
    }

    public async Task<RetentionDto> UpdateAsync(Guid actorId, Guid workspaceId, UpdateRetentionRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var s = await db.RetentionSettings.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        var months = s?.ClosedRequestMonths;
        var auto = s?.AutoPurge ?? false;
        if (req.Clear == true) { months = null; auto = false; }
        if (req.ClosedRequestMonths is { } m)
        {
            if (m is < MinMonths or > MaxMonths) throw new RequestValidationException("invalid_retention", $"Saklama süresi {MinMonths}–{MaxMonths} ay arasında olmalı.");
            months = m;
        }
        if (req.AutoPurge is { } a) auto = a;
        if (auto && months is null) throw new RequestValidationException("retention_not_set", "Otomatik silme için önce saklama süresini belirleyin.");

        if (s is null) db.RetentionSettings.Add(s = new RetentionSettings { WorkspaceId = workspaceId });
        s.ClosedRequestMonths = months;
        s.AutoPurge = auto;
        s.UpdatedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "retention.settings", "workspace", workspaceId.ToString(), new { months, auto });
        await db.SaveChangesAsync(ct);
        return new RetentionDto(months, auto);
    }

    // ============================================================ önizleme ve silme

    private IQueryable<FlowRequest> Closed(Guid workspaceId, int months)
    {
        var cutoff = clock.GetUtcNow().AddMonths(-months);
        return db.FlowRequests.Where(r => r.WorkspaceId == workspaceId && r.Status != RequestStatus.InReview && r.CompletedAt != null && r.CompletedAt < cutoff);
    }

    private IQueryable<FlowRequest> Unprocessed(IQueryable<FlowRequest> q) =>
        q.Where(r => db.AccountingEntries.Any(e => e.RequestId == r.Id && (e.Status == AccountingStatus.Pending || e.Status == AccountingStatus.Failed)));

    public async Task<RetentionPreviewDto> PreviewAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var months = (await db.RetentionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct))?.ClosedRequestMonths
                     ?? throw new ConflictException("retention_not_set", "Önce saklama süresini belirleyin.");
        var all = Closed(workspaceId, months).AsNoTracking();
        var skipped = await Unprocessed(all).CountAsync(ct);
        return new RetentionPreviewDto(await all.CountAsync(ct) - skipped, skipped, months);
    }

    public async Task<PurgeResultDto> PurgeAsync(Guid actorId, Guid workspaceId, bool? confirm, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        if (confirm != true) throw new RequestValidationException("confirmation_required", "Silme geri alınamaz; onaylamak için confirm=true gönderin (önce önizleme alın).");
        var months = (await db.RetentionSettings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct))?.ClosedRequestMonths
                     ?? throw new ConflictException("retention_not_set", "Önce saklama süresini belirleyin.");
        return await PurgeCoreAsync(workspaceId, months, actorId, ct);
    }

    /// <summary>Bakım turu: otomatik silme açık olan çalışma alanları için; tur başına en çok <see cref="MaxPerRun"/> talep silinir (kalanı sonraki turda).</summary>
    public async Task<int> PurgeAutomaticAsync(CancellationToken ct)
    {
        var due = await db.RetentionSettings.AsNoTracking().Where(s => s.AutoPurge && s.ClosedRequestMonths != null).ToListAsync(ct);
        var total = 0;
        foreach (var s in due) total += (await PurgeCoreAsync(s.WorkspaceId, s.ClosedRequestMonths!.Value, null, ct, MaxPerRun)).Purged;
        return total;
    }

    private async Task<PurgeResultDto> PurgeCoreAsync(Guid workspaceId, int months, Guid? actorId, CancellationToken ct, int limit = int.MaxValue)
    {
        var skipped = await Unprocessed(Closed(workspaceId, months)).CountAsync(ct);
        var purged = 0;
        while (purged < limit)
        {
            var batch = await Closed(workspaceId, months).Except(Unprocessed(Closed(workspaceId, months)))
                .OrderBy(r => r.CompletedAt).Select(r => r.Id).Take(Math.Min(BatchSize, limit - purged)).ToListAsync(ct);
            if (batch.Count == 0) break;
            await DeleteAsync(batch, ct);
            purged += batch.Count;
        }
        if (purged > 0)
        {
            audit.Add(workspaceId, null, actorId, "retention.purged", "workspace", workspaceId.ToString(), new { purged, skipped, months, auto = actorId is null });
            await db.SaveChangesAsync(ct);
        }
        return new PurgeResultDto(purged, skipped);
    }

    // ====================================================================== tekil

    public async Task DeleteRequestAsync(Guid actorId, Guid requestId, CancellationToken ct)
    {
        var r = await db.FlowRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct) ?? throw new NotFoundException("Talep bulunamadı.");
        var member = await access.MemberAsync(actorId, r.WorkspaceId, ct);
        if (member.Role != WorkspaceRole.Admin) throw new ForbiddenException("Talebi yalnızca master admin silebilir.");
        if (r.Status == RequestStatus.InReview) throw new ConflictException("request_open", "Açık talep silinemez; önce iptal edin veya sonuçlandırın.");
        if (await db.AccountingEntries.AnyAsync(e => e.RequestId == r.Id && (e.Status == AccountingStatus.Pending || e.Status == AccountingStatus.Failed), ct))
            throw new ConflictException("accounting_unprocessed", "Muhasebe kaydı henüz işlenmemiş; önce muhasebede işlendi olarak işaretleyin.");

        await DeleteAsync([requestId], ct);
        audit.Add(r.WorkspaceId, null, actorId, "request.deleted", "request", requestId.ToString(), new { r.Number, status = RequestService.Status(r.Status) });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Taleplerin kendisi (görev, olay, muhasebe, ek satırları zincirleme) silinir; ardından ek baytları depodan kaldırılır.</summary>
    private async Task DeleteAsync(List<Guid> ids, CancellationToken ct)
    {
        var fileIds = await db.Attachments.Where(a => a.RequestId != null && ids.Contains(a.RequestId.Value)).Select(a => a.Id).ToListAsync(ct);
        await db.FlowRequests.Where(r => ids.Contains(r.Id)).ExecuteDeleteAsync(ct);
        foreach (var f in fileIds) await store.DeleteAsync(f, CancellationToken.None);
    }
}
