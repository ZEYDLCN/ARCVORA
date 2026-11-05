using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>Onaylayıcı çözümü için bir çalışma alanının admin ve grup bilgisini bellekte tutar (motor eşzamanlı çalışır).</summary>
public sealed class FlowDirectory : IFlowDirectory
{
    private readonly IReadOnlyList<Guid> _admins;
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _groups;

    private FlowDirectory(IReadOnlyList<Guid> admins, Dictionary<Guid, IReadOnlyList<Guid>> groups) { _admins = admins; _groups = groups; }

    public IReadOnlyList<Guid> Admins() => _admins;
    public IReadOnlyList<Guid> GroupMembers(Guid groupId) => _groups.GetValueOrDefault(groupId, []);

    /// <summary>Yalnızca tanımda geçen gruplar yüklenir; çalışma alanından çıkmış kişiler grup üyesi sayılmaz.</summary>
    public static async Task<FlowDirectory> LoadAsync(ArcvoraDbContext db, Guid workspaceId, FlowDefinition def, CancellationToken ct)
    {
        var admins = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Admin)
            .Select(m => m.UserId).OrderBy(x => x).ToListAsync(ct);

        var groupIds = def.Steps.Where(s => s.Approvers is { Type: ApproverType.Group, Id: not null }).Select(s => s.Approvers.Id!.Value).Distinct().ToList();
        var groups = new Dictionary<Guid, IReadOnlyList<Guid>>();
        if (groupIds.Count > 0)
        {
            var rows = await (from gm in db.GroupMembers.AsNoTracking()
                              join g in db.Groups.AsNoTracking() on gm.GroupId equals g.Id
                              join m in db.Memberships.AsNoTracking() on new { g.WorkspaceId, gm.UserId } equals new { m.WorkspaceId, m.UserId }
                              where g.WorkspaceId == workspaceId && groupIds.Contains(gm.GroupId)
                              select new { gm.GroupId, gm.UserId }).ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.GroupId)) groups[g.Key] = g.Select(x => x.UserId).Distinct().OrderBy(x => x).ToList();
        }
        return new FlowDirectory(admins, groups);
    }
}
