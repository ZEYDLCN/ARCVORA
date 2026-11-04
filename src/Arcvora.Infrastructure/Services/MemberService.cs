using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

public sealed class MemberService(ArcvoraDbContext db, ProjectAccessService access, AuditLog audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<MemberDto>> ListAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(userId, workspaceId, WorkspaceRole.Reader, ct);
        var rows = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.WorkspaceId == workspaceId)
            .OrderBy(m => m.User!.Email).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<MemberDto> UpsertAsync(Guid actorId, Guid workspaceId, AddMemberRequest req, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var email = AuthService.NormalizeEmail(req.Email);
        if (!Enum.TryParse<WorkspaceRole>(req.Role, true, out var role))
            throw new RequestValidationException("invalid_role", "Rol 'admin', 'editor' veya 'reader' olmalı.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct)
                   ?? throw new RequestValidationException("user_not_found", "Bu e-posta ile kayıtlı kullanıcı yok. Önce kayıt olmalı.");
        var m = await db.Memberships.Include(x => x.User).FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == user.Id, ct);

        if (m is not null && m.Role == WorkspaceRole.Admin && role != WorkspaceRole.Admin)
            await EnsureAnotherAdminAsync(workspaceId, user.Id, ct);

        if (m is null)
        {
            // Gezinme özelliği atanmaz: izlenmeyen User'ın yeniden eklenmeye çalışılmasını engeller.
            m = new Membership { WorkspaceId = workspaceId, UserId = user.Id, CreatedAt = clock.GetUtcNow() };
            db.Memberships.Add(m);
        }
        m.Role = role;
        m.CanAccept = req.CanAccept ?? role != WorkspaceRole.Reader;
        audit.Add(workspaceId, null, actorId, "member.upserted", "user", user.Id.ToString(), new { role = role.ToString(), m.CanAccept });
        await db.SaveChangesAsync(ct);
        return new MemberDto(user.Id, user.Email, user.DisplayName, m.Role.ToString().ToLowerInvariant(),
            m.Role == WorkspaceRole.Admin || m.CanAccept);
    }

    public async Task RemoveAsync(Guid actorId, Guid workspaceId, Guid userId, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                ?? throw new NotFoundException("Üye bulunamadı.");
        if (m.Role == WorkspaceRole.Admin) await EnsureAnotherAdminAsync(workspaceId, userId, ct);
        db.Memberships.Remove(m);
        audit.Add(workspaceId, null, actorId, "member.removed", "user", userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureAnotherAdminAsync(Guid workspaceId, Guid exceptUserId, CancellationToken ct)
    {
        var others = await db.Memberships.CountAsync(x => x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Admin && x.UserId != exceptUserId, ct);
        if (others == 0) throw new ConflictException("last_admin", "Çalışma alanında en az bir yönetici kalmalı.");
    }

    private static MemberDto ToDto(Membership m) => new(m.UserId, m.User?.Email ?? "", m.User?.DisplayName ?? "",
        m.Role.ToString().ToLowerInvariant(), m.Role == WorkspaceRole.Admin || m.CanAccept);
}
