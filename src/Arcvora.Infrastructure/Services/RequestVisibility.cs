using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Bir talebi kimin görebileceğinin tek kuralı: talep eden, master admin, görev atananı veya (ör. muhasebe grubu gibi) kancanın izin verdiği kişi.
/// Çalışma alanı üyesi olmayan ya da göremeyen herkes için 404 (talebin varlığı sızdırılmaz). Ayrıntı, ek ve PDF aynı kuralı kullanır.
/// </summary>
public sealed class RequestVisibility(ArcvoraDbContext db, IRequestHooks hooks)
{
    public async Task<VisibleRequest> LoadAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var r = await db.FlowRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId, ct) ?? throw new NotFoundException("Talep bulunamadı.");
        var member = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.WorkspaceId == r.WorkspaceId && m.UserId == userId, ct)
                     ?? throw new NotFoundException("Talep bulunamadı.");
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(t => t.RequestId == r.Id).OrderBy(t => t.StepIndex).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);

        var visible = r.RequesterId == userId || member.Role == WorkspaceRole.Admin || tasks.Any(t => t.AssigneeId == userId)
                      || await hooks.CanViewAsync(userId, r, ct);
        if (!visible) throw new NotFoundException("Talep bulunamadı.");
        return new VisibleRequest(r, member, tasks);
    }
}

public sealed record VisibleRequest(FlowRequest Request, Membership Member, IReadOnlyList<ApprovalTask> Tasks);
