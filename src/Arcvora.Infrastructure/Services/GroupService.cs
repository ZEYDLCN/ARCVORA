using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>Kişi grupları (onaylayıcı / muhasebe grubu). Yönetim yalnızca master admin içindir.</summary>
public sealed class GroupService(ArcvoraDbContext db, ProjectAccessService access, AuditLog audit, TimeProvider clock)
{
    private const int MaxName = 100;

    /// <summary>Ad karşılaştırması büyük/küçük harf ve Türkçe i/İ/ı farkına duyarsızdır ("İK" = "ik").</summary>
    public static string Fold(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Replace('İ', 'i').Replace('I', 'i').Replace('ı', 'i').ToLowerInvariant();

    public async Task<IReadOnlyList<GroupDto>> ListAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var groups = await db.Groups.AsNoTracking().Where(g => g.WorkspaceId == workspaceId).OrderBy(g => g.Name).ToListAsync(ct);
        var ids = groups.Select(g => g.Id).ToList();
        var members = (await db.GroupMembers.AsNoTracking().Where(gm => ids.Contains(gm.GroupId)).ToListAsync(ct))
            .GroupBy(gm => gm.GroupId).ToDictionary(g => g.Key, g => g.Select(x => x.UserId).OrderBy(x => x).ToList());
        return groups.Select(g => new GroupDto(g.Id, g.Name, members.GetValueOrDefault(g.Id, []))).ToList();
    }

    public async Task<GroupDto> CreateAsync(Guid actorId, Guid workspaceId, string? name, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var clean = await ValidNameAsync(workspaceId, name, null, ct);
        var g = new Group { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = clean, CreatedAt = clock.GetUtcNow() };
        db.Groups.Add(g);
        audit.Add(workspaceId, null, actorId, "group.created", "group", g.Id.ToString(), new { g.Name });
        await db.SaveChangesAsync(ct);
        return new GroupDto(g.Id, g.Name, []);
    }

    public async Task<GroupDto> RenameAsync(Guid actorId, Guid workspaceId, Guid groupId, string? name, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var g = await FindAsync(workspaceId, groupId, ct);
        g.Name = await ValidNameAsync(workspaceId, name, groupId, ct);
        audit.Add(workspaceId, null, actorId, "group.renamed", "group", groupId.ToString(), new { g.Name });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(g, ct);
    }

    /// <summary>Grubun üyelerini verilen listeyle değiştirir. Yalnızca çalışma alanı üyeleri eklenebilir.</summary>
    public async Task<GroupDto> SetMembersAsync(Guid actorId, Guid workspaceId, Guid groupId, IReadOnlyList<Guid>? userIds, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var g = await FindAsync(workspaceId, groupId, ct);
        var wanted = (userIds ?? []).Distinct().ToList();
        var valid = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId && wanted.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
        if (valid.Count != wanted.Count)
            throw new RequestValidationException("invalid_member", "Gruba yalnızca bu çalışma alanının üyeleri eklenebilir.");

        var current = await db.GroupMembers.Where(gm => gm.GroupId == groupId).ToListAsync(ct);
        db.GroupMembers.RemoveRange(current.Where(c => !wanted.Contains(c.UserId)));
        foreach (var id in wanted.Where(w => current.All(c => c.UserId != w)))
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, UserId = id });
        audit.Add(workspaceId, null, actorId, "group.members_set", "group", groupId.ToString(), new { count = wanted.Count });
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(g, ct);
    }

    public async Task DeleteAsync(Guid actorId, Guid workspaceId, Guid groupId, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var g = await FindAsync(workspaceId, groupId, ct);
        db.Groups.Remove(g);
        audit.Add(workspaceId, null, actorId, "group.deleted", "group", groupId.ToString(), new { g.Name });
        await db.SaveChangesAsync(ct);
    }

    private async Task<Group> FindAsync(Guid workspaceId, Guid groupId, CancellationToken ct) =>
        await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId && g.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Grup bulunamadı.");

    private async Task<string> ValidNameAsync(Guid workspaceId, string? name, Guid? exceptId, CancellationToken ct)
    {
        var clean = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length is 0 or > MaxName)
            throw new RequestValidationException("invalid_name", $"Grup adı 1–{MaxName} karakter olmalı.");
        var folded = Fold(clean);
        var names = await db.Groups.AsNoTracking().Where(g => g.WorkspaceId == workspaceId && g.Id != exceptId).Select(g => g.Name).ToListAsync(ct);
        if (names.Any(n => Fold(n) == folded))
            throw new ConflictException("group_name_taken", "Bu adla bir grup zaten var.");
        return clean;
    }

    private async Task<GroupDto> ToDtoAsync(Group g, CancellationToken ct) =>
        new(g.Id, g.Name, await db.GroupMembers.AsNoTracking().Where(gm => gm.GroupId == g.Id).Select(gm => gm.UserId).OrderBy(x => x).ToListAsync(ct));
}
