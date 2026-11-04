using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

public enum Permission { Read, Edit, Accept, Admin }

public sealed record ProjectAccessContext(Project Project, Membership Membership, Guid UserId)
{
    public bool Can(Permission p) => p switch
    {
        Permission.Read => true,
        Permission.Edit => Membership.Role >= WorkspaceRole.Editor,
        Permission.Accept => Membership.Role == WorkspaceRole.Admin || (Membership.Role == WorkspaceRole.Editor && Membership.CanAccept),
        Permission.Admin => Membership.Role == WorkspaceRole.Admin,
        _ => false,
    };
}

/// <summary>
/// Her proje işlemi çalışma alanı üyeliğini ve rolü sunucu tarafında doğrular. Üyesi olunmayan çalışma alanındaki
/// proje için 404 döner (varlığı sızdırılmaz); üye ama yetkisiz ise 403.
/// </summary>
public sealed class ProjectAccessService(ArcvoraDbContext db)
{
    public async Task<ProjectAccessContext> RequireAsync(Guid userId, Guid projectId, Permission permission,
        bool track = true, CancellationToken ct = default)
    {
        // AsNoTracking sorgunun tamamını etkiler: izleme gerekiyorsa hiçbir kolda kullanılmamalıdır
        // (aksi hâlde Project.HeadNumber değişikliği sessizce yazılmaz).
        var query = from p in db.Projects
                    join m in db.Memberships on p.WorkspaceId equals m.WorkspaceId
                    where p.Id == projectId && m.UserId == userId
                    select new { Project = p, Membership = m };
        if (!track) query = query.AsNoTracking();
        var row = await query.FirstOrDefaultAsync(ct);

        if (row is null) throw new NotFoundException("Proje bulunamadı.");

        var ctx = new ProjectAccessContext(row.Project, row.Membership, userId);
        if (!ctx.Can(permission))
            throw new ForbiddenException(permission == Permission.Accept
                ? "Değişiklik kabul etme yetkiniz yok."
                : "Bu işlem için yetkiniz yok.");
        return ctx;
    }

    public async Task<WorkspaceRole> RequireWorkspaceRoleAsync(Guid userId, Guid workspaceId, WorkspaceRole min, CancellationToken ct)
    {
        var m = await db.Memberships.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                ?? throw new NotFoundException("Çalışma alanı bulunamadı.");
        if (m.Role < min) throw new ForbiddenException();
        return m.Role;
    }
}
