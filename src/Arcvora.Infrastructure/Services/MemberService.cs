using Arcvora.Core.Flows;
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
        var user = new User
        {
            Id = Guid.NewGuid(), Email = email, DisplayName = name, PasswordHash = hasher.Hash(req.Password!), MustChangePassword = true,
            ManagedByWorkspaceId = workspaceId, CreatedAt = now,
        };
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

        // Aynı anda bir onay kararı verilirse talep satırı (xmin) çakışır: işlem baştan, güncel veriyle yinelenir.
        for (var attempt = 1; ; attempt++)
        {
            var m = await db.Memberships.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                    ?? throw new NotFoundException("Üye bulunamadı.");
            if (m.Role == WorkspaceRole.Admin) await EnsureAnotherAdminAsync(workspaceId, userId, ct);
            db.Memberships.Remove(m);

            // Çalışma alanından çıkan kişi gruplardan düşer ve ona bağlı çalışanların yöneticisi boşalır.
            var groupIds = db.Groups.Where(g => g.WorkspaceId == workspaceId).Select(g => g.Id);
            db.GroupMembers.RemoveRange(await db.GroupMembers.Where(gm => gm.UserId == userId && groupIds.Contains(gm.GroupId)).ToListAsync(ct));
            foreach (var report in await db.Memberships.Where(x => x.WorkspaceId == workspaceId && x.ManagerId == userId).ToListAsync(ct))
                report.ManagerId = null;

            await ReassignPendingTasksAsync(workspaceId, userId, ct);
            audit.Add(workspaceId, null, actorId, "member.removed", "user", userId.ToString());
            try { await db.SaveChangesAsync(ct); return; }
            catch (DbUpdateConcurrencyException) when (attempt < 3) { db.ChangeTracker.Clear(); }
        }
    }

    /// <summary>
    /// Çıkarılan kişinin bekleyen onay görevleri takılı kalmaz. Aynı adımda başka bekleyen onaylayıcı varsa o devam eder (kişinin payı düşer);
    /// kimse kalmadıysa görev master admin'e (talep edeni hariç) yönlenir ve "assignee_removed" olarak işaretlenir.
    /// "Hepsi onaylamalı" adımlarında kişinin yerine tek bir master admin onayı beklenir.
    /// </summary>
    private async Task ReassignPendingTasksAsync(Guid workspaceId, Guid removedId, CancellationToken ct)
    {
        var mine = await db.ApprovalTasks.Where(t => t.WorkspaceId == workspaceId && t.AssigneeId == removedId && t.Status == ApprovalStatus.Pending).ToListAsync(ct);
        if (mine.Count == 0) return;

        var admins = await db.Memberships.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.Role == WorkspaceRole.Admin && x.UserId != removedId)
            .Select(x => x.UserId).OrderBy(x => x).ToListAsync(ct);
        var now = clock.GetUtcNow();

        foreach (var step in mine.GroupBy(t => new { t.RequestId, t.StepIndex }))
        {
            var task = step.First();
            var request = await db.FlowRequests.FirstAsync(r => r.Id == step.Key.RequestId, ct);
            foreach (var t in step) t.Status = ApprovalStatus.Skipped;
            if (request.Status != RequestStatus.InReview) continue;

            // Talep satırına dokunmak, eşzamanlı bir kararla çakışmayı (xmin) yakalar.
            request.UpdatedAt = now;
            var siblings = await db.ApprovalTasks.Where(t => t.RequestId == request.Id && t.StepIndex == step.Key.StepIndex).ToListAsync(ct);
            var remaining = siblings.Where(s => s.Status == ApprovalStatus.Pending).ToList();      // izlenen (güncel) durumlar

            List<Guid> replacements = [];
            if (remaining.Count == 0)
            {
                var candidates = admins.Where(a => a != request.RequesterId).ToList();
                if (candidates.Count == 0) candidates = [.. admins];            // yalnızca talep eden admin kaldıysa o onaylayabilir
                replacements = task.Mode == ApprovalMode.All ? candidates.Take(1).ToList() : candidates;
                foreach (var a in replacements)
                    db.ApprovalTasks.Add(new ApprovalTask
                    {
                        Id = Guid.NewGuid(), RequestId = request.Id, WorkspaceId = workspaceId, StepIndex = task.StepIndex, StepId = task.StepId,
                        StepName = task.StepName, Mode = task.Mode, AssigneeId = a, Fallback = "assignee_removed", CreatedAt = now,
                    });
            }
            db.RequestEvents.Add(new RequestEvent
            {
                RequestId = request.Id, WorkspaceId = workspaceId, At = now, Kind = RequestEventKinds.ApproverRemoved,
                DetailJson = Arcvora.Core.Model.ArcvoraJson.Serialize(new { stepId = task.StepId, stepName = task.StepName, removedId, replacements }),
            });
        }
    }

    /// <summary>
    /// Master admin, KENDİ açtığı bir hesabın parolasını sıfırlar (yeni geçici parola verir; kullanıcı ilk girişte değiştirir).
    /// Kendi kaydıyla açılmış veya başka bir çalışma alanının açtığı hesaplara dokunulamaz: aksi halde bir çalışma alanı admin'i,
    /// başka şirkete de üye olan bir hesabı ele geçirebilirdi.
    /// </summary>
    public async Task<MemberDto> ResetPasswordAsync(Guid actorId, Guid workspaceId, Guid userId, string? password, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(actorId, workspaceId, WorkspaceRole.Admin, ct);
        var m = await db.Memberships.Include(x => x.User).FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct)
                ?? throw new NotFoundException("Üye bulunamadı.");
        var user = m.User!;
        if (user.ManagedByWorkspaceId != workspaceId)
            throw new ConflictException("not_managed", "Bu hesap kendi kaydıyla (veya başka bir çalışma alanı tarafından) açılmış; parolasını yalnızca sahibi değiştirebilir.");
        AuthService.ValidatePassword(password);

        user.PasswordHash = hasher.Hash(password!);
        user.MustChangePassword = true;
        audit.Add(workspaceId, null, actorId, "user.password_reset", "user", userId.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(m);
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
        RoleNames.ToApi(m.Role), m.Role == WorkspaceRole.Admin || m.CanAccept, m.ManagerId, m.User?.MustChangePassword ?? false,
        m.User?.ManagedByWorkspaceId == m.WorkspaceId);
}
