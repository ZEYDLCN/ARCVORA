using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Validation;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>Veritabanına yazılmaya hazır, henüz kalıcı olmayan öneri.</summary>
public sealed record PendingProposal(
    string Kind, string Title, string? RequestText, Guid BaseVersionId, int BaseNumber, ModelPatch Patch, ImpactReport Impact,
    IReadOnlyList<ValidationFinding> NewFindings, string Provider, bool IsAi, string? Note, int TokensUsed, int RepairsUsed);

public sealed class ProposalService(ArcvoraDbContext db, ProjectAccessService access, VersionStore versions, AuditLog audit, TimeProvider clock)
{
    /// <summary>Öneriyi bağlama ekler; SaveChanges çağıranın sorumluluğundadır (iş durumuyla aynı işlemde yazılsın diye).</summary>
    public Proposal Stage(Guid projectId, Guid createdBy, PendingProposal p, Guid? jobId)
    {
        var entity = new Proposal
        {
            Id = Guid.NewGuid(), ProjectId = projectId, Kind = p.Kind, Title = p.Title, RequestText = p.RequestText,
            BaseVersionId = p.BaseVersionId, BaseNumber = p.BaseNumber,
            PatchJson = ArcvoraJson.Serialize(p.Patch), ImpactJson = ArcvoraJson.Serialize(p.Impact),
            FindingsJson = ArcvoraJson.Serialize(p.NewFindings), Provider = p.Provider, IsAi = p.IsAi, Note = p.Note,
            TokensUsed = p.TokensUsed, RepairsUsed = p.RepairsUsed, Status = ProposalStatus.Pending,
            CreatedBy = createdBy, CreatedAt = clock.GetUtcNow(), JobId = jobId,
        };
        db.Proposals.Add(entity);
        return entity;
    }

    public async Task<IReadOnlyList<ProposalSummary>> ListAsync(Guid userId, Guid projectId, string? status, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var query = db.Proposals.AsNoTracking().Where(p => p.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ProposalStatus>(status, true, out var st))
            query = query.Where(p => p.Status == st);
        var rows = await query.OrderByDescending(p => p.CreatedAt).Take(100).ToListAsync(ct);
        return rows.Select(p => ToSummary(p, ctx.Project.HeadNumber)).ToList();
    }

    public async Task<ProposalDetail> GetAsync(Guid userId, Guid projectId, Guid proposalId, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var p = await Load(projectId, proposalId, tracked: false, ct);
        var patch = ArcvoraJson.Deserialize<ModelPatch>(p.PatchJson).WithDefaultGroups();
        var baseModel = VersionStore.Load(await versions.GetByIdAsync(projectId, p.BaseVersionId, ct));
        var deps = GroupDependencies.Compute(baseModel, patch);

        var groups = patch.Groups.Select(g => new GroupInfo(g.Id, g.Title, g.Description,
            patch.Operations.Count(o => o.GroupId == g.Id), deps[g.Id].OrderBy(x => x, StringComparer.Ordinal).ToList())).ToList();

        return new ProposalDetail(ToSummary(p, ctx.Project.HeadNumber), patch, ArcvoraJson.Deserialize<ImpactReport>(p.ImpactJson),
            ArcvoraJson.Deserialize<List<ValidationFinding>>(p.FindingsJson), groups);
    }

    public async Task<AcceptResult> AcceptAsync(Guid userId, Guid projectId, Guid proposalId, AcceptRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Accept, track: true, ct);
        var proposal = await Load(projectId, proposalId, tracked: true, ct);

        // Aynı kabulün tekrarı yeni sürüm üretmez.
        if (proposal.Status == ProposalStatus.Accepted) return await AlreadyAccepted(ctx, proposal, ct);
        if (proposal.Status == ProposalStatus.Rejected)
            throw new ConflictException("proposal_rejected", "Bu öneri reddedilmiş.");

        if (req.BaseVersion is { } clientBase && clientBase != proposal.BaseNumber)
            throw new ConflictException("reassessment_required",
                $"Öneri v{proposal.BaseNumber} üzerine hazırlandı, ancak v{clientBase} üzerinden kabul edilmek isteniyor.",
                new { proposalBase = proposal.BaseNumber, requestedBase = clientBase, headVersion = ctx.Project.HeadNumber });

        // Son yazan diğerlerinin değişikliğini ezemez: temel sürüm hâlâ güncel olmalı (§16.2).
        if (proposal.BaseNumber != ctx.Project.HeadNumber)
        {
            proposal.Status = ProposalStatus.Stale;
            await db.SaveChangesAsync(ct);
            throw new ConflictException("reassessment_required",
                $"Model bu arada v{proposal.BaseNumber} → v{ctx.Project.HeadNumber} olarak değişti. Öneri yeniden değerlendirilmeli.",
                new { proposalBase = proposal.BaseNumber, headVersion = ctx.Project.HeadNumber });
        }

        var patch = ArcvoraJson.Deserialize<ModelPatch>(proposal.PatchJson).WithDefaultGroups();
        if (patch.IsEmpty)
            throw new RequestValidationException("empty_patch", "Bu öneri uygulanabilir bir model değişikliği içermiyor; reddedebilir veya yapılandırılmış düzenleyiciyi kullanabilirsiniz.");

        var baseVersion = await versions.GetByIdAsync(projectId, proposal.BaseVersionId, ct);
        var baseModel = VersionStore.Load(baseVersion);

        var selection = GroupDependencies.CheckSelection(baseModel, patch, req.GroupIds);
        if (!selection.Ok)
            throw new RequestValidationException("invalid_selection", "Seçilen değişiklik grupları tutarlı değil.", selection.Errors);

        var applied = ModelPatcher.Apply(baseModel, patch, selection.Selected, confirmApplied: true);
        if (!applied.Ok) throw new RequestValidationException("patch_invalid", "Öneri uygulanamadı.", applied.Errors);

        var partial = selection.Selected.Count < patch.Groups.Count;
        var summary = partial ? $"{proposal.Title} (kısmi kabul: {selection.Selected.Count}/{patch.Groups.Count} grup)" : proposal.Title;

        ModelVersion created;
        try
        {
            created = await versions.CommitAsync(ctx.Project, baseModel, applied.Model!, userId, summary, VersionKinds.Proposal, proposal.Id,
                v =>
                {
                    proposal.Status = ProposalStatus.Accepted;
                    proposal.AcceptedVersionId = v.Id;
                    proposal.AcceptedGroupsJson = ArcvoraJson.Serialize(selection.Selected.OrderBy(x => x, StringComparer.Ordinal).ToList());
                    proposal.DecidedAt = v.CreatedAt;
                    proposal.DecidedBy = userId;
                    audit.Add(ctx.Project.WorkspaceId, projectId, userId, "proposal.accepted", "proposal", proposal.Id.ToString(),
                        new { version = v.Number, groups = selection.Selected, partial });
                }, ct);
        }
        catch (ConflictException)
        {
            // Aynı öneriyi eşzamanlı kabul eden diğer istek kazanmış olabilir: sonuç aynı sürümdür.
            db.ChangeTracker.Clear();
            var fresh = await db.Proposals.AsNoTracking().FirstOrDefaultAsync(p => p.Id == proposalId, ct);
            if (fresh is { Status: ProposalStatus.Accepted })
            {
                var freshProject = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
                return await AlreadyAccepted(new ProjectAccessContext(freshProject, ctx.Membership, userId), fresh, ct);
            }
            throw;
        }

        return new AcceptResult(VersionStore.ToInfo(created), ToSummary(proposal, ctx.Project.HeadNumber), false);
    }

    public async Task<ProposalSummary> RejectAsync(Guid userId, Guid projectId, Guid proposalId, RejectRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Accept, track: true, ct);
        var p = await Load(projectId, proposalId, tracked: true, ct);
        if (p.Status == ProposalStatus.Accepted) throw new ConflictException("proposal_accepted", "Kabul edilmiş öneri reddedilemez.");
        if (p.Status == ProposalStatus.Rejected) return ToSummary(p, ctx.Project.HeadNumber);

        p.Status = ProposalStatus.Rejected;
        p.DecidedAt = clock.GetUtcNow();
        p.DecidedBy = userId;
        audit.Add(ctx.Project.WorkspaceId, projectId, userId, "proposal.rejected", "proposal", p.Id.ToString(),
            new { note = req.Note?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 500)] : null });
        await db.SaveChangesAsync(ct);
        return ToSummary(p, ctx.Project.HeadNumber);
    }

    private async Task<AcceptResult> AlreadyAccepted(ProjectAccessContext ctx, Proposal proposal, CancellationToken ct)
    {
        var v = await versions.GetByIdAsync(ctx.Project.Id, proposal.AcceptedVersionId!.Value, ct);
        return new AcceptResult(VersionStore.ToInfo(v), ToSummary(proposal, ctx.Project.HeadNumber), true);
    }

    private async Task<Proposal> Load(Guid projectId, Guid proposalId, bool tracked, CancellationToken ct)
    {
        var q = tracked ? db.Proposals : db.Proposals.AsNoTracking();
        return await q.FirstOrDefaultAsync(p => p.Id == proposalId && p.ProjectId == projectId, ct)
               ?? throw new NotFoundException("Öneri bulunamadı.");
    }

    public static ProposalSummary ToSummary(Proposal p, int headNumber)
    {
        var ops = 0;
        try { ops = ArcvoraJson.Deserialize<ModelPatch>(p.PatchJson).Operations.Count; } catch (System.Text.Json.JsonException) { }
        var stale = p.Status == ProposalStatus.Stale || (p.Status == ProposalStatus.Pending && p.BaseNumber != headNumber);
        return new ProposalSummary(p.Id, p.Kind, p.Title, p.RequestText,
            (stale ? ProposalStatus.Stale : p.Status).ToString().ToLowerInvariant(),
            p.BaseNumber, p.Provider, p.IsAi, p.Note, p.TokensUsed, p.RepairsUsed, p.CreatedAt, p.DecidedAt, p.AcceptedVersionId, ops, stale);
    }
}
