using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

public sealed class MemberService(ArcvoraDbContext db, ProjectAccessService access, AuditLog audit, PasswordHasher hasher, TimeProvider clock)
{
    private const string RoleMessage = "Rol 'admin', 'editor', 'reader' veya 'app_user' olmalı.";

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
        if (!RoleNames.TryParse(req.Role, out var role))
            throw new RequestValidationException("invalid_role", RoleMessage);

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
        m.CanAccept = req.CanAccept ?? role is WorkspaceRole.Editor or WorkspaceRole.Admin;
        audit.Add(workspaceId, null, actorId, "member.upserted", "user", user.Id.ToString(), new { role = RoleNames.ToApi(role), m.CanAccept });
        await db.SaveChangesAsync(ct);
        return new MemberDto(user.Id, user.Email, user.DisplayName, RoleNames.ToApi(m.Role), m.Role == WorkspaceRole.Admin || m.CanAccept, m.ManagerId);
    }

    /// <summary>Master admin yeni bir hesap açar ve çalışma alanına ekler (self-servis kayıt kapalıyken de çalışır).</summary>
    public async Task<MemberDto> CreateUserAsync(Guid actorId, Guid workspaceId, CreateUserRequest req, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var email = AuthService.NormalizeEmail(req.Email);
        AuthService.ValidatePassword(req.Password);
        var name = (req.DisplayName ?? "").Trim();
        if (name.Length == 0) name = email.Split('@')[0];
        if (name.Length > 200) throw new RequestValidationException("invalid_name", "Ad çok uzun.");
        var role = WorkspaceRole.AppUser;
        if (!string.IsNullOrWhiteSpace(req.Role) && !RoleNames.TryParse(req.Role, out role))
            throw new RequestValidationException("invalid_role", RoleMessage);
        if (req.ManagerId is { } managerId) await ValidateManagerAsync(workspaceId, null, managerId, ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictException("email_taken", "Bu e-posta adresiyle zaten bir hesap var.");

        var now = clock.GetUtcNow();
        var user = new User { Id = Guid.NewGuid(), Email = email, DisplayName = name, PasswordHash = hasher.Hash(req.Password!), MustChangePassword = true, CreatedAt = now };
        var m = new Membership
        {
            WorkspaceId = workspaceId, UserId = user.Id, Role = role, ManagerId = req.ManagerId, CreatedAt = now,
            CanAccept = role is WorkspaceRole.Editor or WorkspaceRole.Admin,
        };
        db.Users.Add(user);
        db.Memberships.Add(m);
        audit.Add(workspaceId, null, actorId, "user.created", "user", user.Id.ToString(), new { role = RoleNames.ToApi(role) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("email_taken", "Bu e-posta adresiyle zaten bir hesap var.");
        }
        m.User = user;
        return ToDto(m);
    }

    public async Task<MemberDto> UpdateAsync(Guid actorId, Guid workspaceId, Guid userId, UpdateMemberRequest req, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var m = await db.Memberships.Include(x => x.User).FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                ?? throw new NotFoundException("Üye bulunamadı.");

        if (req.Role is not null)
        {
            if (!RoleNames.TryParse(req.Role, out var role)) throw new RequestValidationException("invalid_role", RoleMessage);
            if (m.Role == WorkspaceRole.Admin && role != WorkspaceRole.Admin) await EnsureAnotherAdminAsync(workspaceId, userId, ct);
            m.Role = role;
            if (req.CanAccept is null) m.CanAccept = role is WorkspaceRole.Editor or WorkspaceRole.Admin;
        }
        if (req.CanAccept is { } canAccept) m.CanAccept = canAccept;
        if (req.ClearManager == true) m.ManagerId = null;
        else if (req.ManagerId is { } managerId)
        {
            await ValidateManagerAsync(workspaceId, userId, managerId, ct);
            m.ManagerId = managerId;
        }
        if (req.DisplayName is not null)
        {
            var name = req.DisplayName.Trim();
            if (name.Length is 0 or > 200) throw new RequestValidationException("invalid_name", "Ad 1–200 karakter olmalı.");
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            user.DisplayName = name;
            m.User = user;
        }
        audit.Add(workspaceId, null, actorId, "member.updated", "user", userId.ToString(),
            new { role = RoleNames.ToApi(m.Role), m.CanAccept, m.ManagerId });
        await db.SaveChangesAsync(ct);
        return ToDto(m);
    }

    public async Task RemoveAsync(Guid actorId, Guid workspaceId, Guid userId, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var m = await db.Memberships.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                ?? throw new NotFoundException("Üye bulunamadı.");
        if (m.Role == WorkspaceRole.Admin) await EnsureAnotherAdminAsync(workspaceId, userId, ct);
        db.Memberships.Remove(m);

        // Çalışma alanından çıkan kişi gruplardan düşer ve ona bağlı çalışanların yöneticisi boşalır.
        var groupIds = db.Groups.Where(g => g.WorkspaceId == workspaceId).Select(g => g.Id);
        db.GroupMembers.RemoveRange(await db.GroupMembers.Where(gm => gm.UserId == userId && groupIds.Contains(gm.GroupId)).ToListAsync(ct));
        foreach (var report in await db.Memberships.Where(x => x.WorkspaceId == workspaceId && x.ManagerId == userId).ToListAsync(ct))
            report.ManagerId = null;

        audit.Add(workspaceId, null, actorId, "member.removed", "user", userId.ToString());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Yönetici aynı çalışma alanının üyesi olmalı, kişi kendisi olamaz ve yönetici zincirinde döngü oluşmamalı.</summary>
    private async Task ValidateManagerAsync(Guid workspaceId, Guid? userId, Guid managerId, CancellationToken ct)
    {
        if (userId == managerId) throw new RequestValidationException("invalid_manager", "Kişi kendi yöneticisi olamaz.");
        var map = await db.Memberships.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .ToDictionaryAsync(x => x.UserId, x => x.ManagerId, ct);
        if (!map.ContainsKey(managerId))
            throw new RequestValidationException("invalid_manager", "Yönetici bu çalışma alanının üyesi olmalı.");
        // managerId'den yukarı yürü: userId'ye rastlarsak döngü olur.
        var seen = new HashSet<Guid>();
        for (Guid? cur = managerId; cur is { } id && seen.Add(id); cur = map.GetValueOrDefault(id))
            if (userId is { } u && id == u) throw new RequestValidationException("invalid_manager", "Yönetici zincirinde döngü oluşur.");
    }

    private async Task EnsureAnotherAdminAsync(Guid workspaceId, Guid exceptUserId, CancellationToken ct)
    {
        var others = await db.Memberships.CountAsync(x => x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Admin && x.UserId != exceptUserId, ct);
        if (others == 0) throw new ConflictException("last_admin", "Çalışma alanında en az bir yönetici kalmalı.");
    }

    private static MemberDto ToDto(Membership m) => new(m.UserId, m.User?.Email ?? "", m.User?.DisplayName ?? "",
        RoleNames.ToApi(m.Role), m.Role == WorkspaceRole.Admin || m.CanAccept, m.ManagerId, m.User?.MustChangePassword ?? false);
}
