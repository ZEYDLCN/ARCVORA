using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Akış uygulamalarına erişim: üyelik her istekte sunucuda doğrulanır. Üye olunmayan çalışma alanındaki kayıt için 404
/// (varlık sızdırılmaz); üye ama yetkisiz ise 403.
/// </summary>
public sealed class AppAccessService(ArcvoraDbContext db)
{
    public async Task<Membership> MemberAsync(Guid userId, Guid workspaceId, CancellationToken ct) =>
        await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct)
        ?? throw new NotFoundException("Çalışma alanı bulunamadı.");

    public async Task<Membership> RequireAdminAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var m = await MemberAsync(userId, workspaceId, ct);
        if (m.Role != WorkspaceRole.Admin) throw new ForbiddenException("Bu işlem için master admin yetkisi gerekir.");
        return m;
    }

    public async Task<(AppDefinition App, Membership Member)> LoadAppAsync(Guid userId, Guid appId, bool track, CancellationToken ct)
    {
        var q = from a in db.Apps
                join m in db.Memberships on a.WorkspaceId equals m.WorkspaceId
                where a.Id == appId && m.UserId == userId
                select new { App = a, Member = m };
        if (!track) q = q.AsNoTracking();
        var row = await q.FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Uygulama bulunamadı.");
        return (row.App, row.Member);
    }

    /// <summary>Master admin her uygulamayı kullanabilir; diğerleri yalnızca kendilerine (veya gruplarına) yetki verilenleri.</summary>
    public async Task<bool> CanUseAsync(Guid userId, Membership member, Guid appId, CancellationToken ct)
    {
        if (member.Role == WorkspaceRole.Admin) return true;
        var groupIds = db.GroupMembers.Where(gm => gm.UserId == userId).Select(gm => gm.GroupId);
        return await db.AppGrants.AnyAsync(g => g.AppId == appId
            && ((g.Kind == GrantKinds.User && g.SubjectId == userId) || (g.Kind == GrantKinds.Group && groupIds.Contains(g.SubjectId))), ct);
    }
}
