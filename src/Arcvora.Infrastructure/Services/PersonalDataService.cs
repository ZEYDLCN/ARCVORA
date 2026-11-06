using System.Security.Cryptography;
using System.Text.Json;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

public sealed record PersonalDataDto(DateTimeOffset ExportedAt, JsonElement User, JsonElement Requests, JsonElement Decisions, JsonElement Delegations, JsonElement Files, JsonElement Notifications);

/// <summary>
/// KVKK: kişinin kendi verisini (veya master admin'in bir üyeninkini) dışa aktarması ve ayrılan kullanıcının anonimleştirilmesi.
/// Dışa aktarımda parola özeti veya başka bir gizli değer yer almaz. Anonimleştirme yalnızca bu çalışma alanının açtığı ve artık hiçbir çalışma
/// alanında üye olmayan hesaplar içindir; talep geçmişi bozulmaz, kişi "Silinmiş kullanıcı" olarak görünür.
/// </summary>
public sealed class PersonalDataService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, TimeProvider clock)
{
    public const string AnonymousName = "Silinmiş kullanıcı";

    public async Task<PersonalDataDto> ExportAsync(Guid actorId, Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var actor = await access.MemberAsync(actorId, workspaceId, ct);
        if (actorId != userId && actor.Role != WorkspaceRole.Admin) throw new ForbiddenException("Yalnızca kendi verinizi dışa aktarabilirsiniz.");
        var m = await db.Memberships.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, ct) ?? throw new NotFoundException("Üye bulunamadı.");
        var u = m.User!;
        var managerName = m.ManagerId is { } mid ? await db.Users.AsNoTracking().Where(x => x.Id == mid).Select(x => x.DisplayName).FirstOrDefaultAsync(ct) : null;

        var requests = await db.FlowRequests.AsNoTracking().Where(r => r.WorkspaceId == workspaceId && r.RequesterId == userId).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var appNames = await db.Apps.AsNoTracking().Where(a => a.WorkspaceId == workspaceId).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var versions = await db.AppVersions.AsNoTracking().Where(v => requests.Select(r => r.AppVersionId).Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => AppDesignService.Deserialize(v.DefinitionJson), ct);
        var people = await db.Users.AsNoTracking().Where(x => db.Memberships.Any(mm => mm.WorkspaceId == workspaceId && mm.UserId == x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var requestRows = requests.Select(r =>
        {
            var values = FlowValues.FromJson(r.DataJson);
            var fields = versions.TryGetValue(r.AppVersionId, out var def)
                ? def.Fields.Where(f => values.ContainsKey(f.Key)).Select(f => new { label = f.Label, value = FlowDisplay.Format(f, values[f.Key], people) }).ToList()
                : [];
            return new { id = r.Id, number = r.Number, app = appNames.GetValueOrDefault(r.AppId), status = RequestService.Status(r.Status), createdAt = r.CreatedAt, completedAt = r.CompletedAt, summary = r.Summary, fields };
        }).ToList();

        var decisions = await (from t in db.ApprovalTasks.AsNoTracking()
                               join r in db.FlowRequests.AsNoTracking() on t.RequestId equals r.Id
                               where t.WorkspaceId == workspaceId && t.DecidedBy == userId
                               orderby t.DecidedAt
                               select new { requestId = r.Id, number = r.Number, step = t.StepName, decision = t.Status == ApprovalStatus.Approved ? "approved" : "rejected", comment = t.Comment, at = t.DecidedAt, onBehalfOf = t.AssigneeId != userId ? t.AssigneeId : (Guid?)null }).ToListAsync(ct);

        var delegations = await db.Delegations.AsNoTracking().Where(d => d.WorkspaceId == workspaceId && (d.FromUserId == userId || d.ToUserId == userId)).OrderBy(d => d.StartsOn)
            .Select(d => new { id = d.Id, from = d.FromUserId, to = d.ToUserId, startsOn = d.StartsOn, endsOn = d.EndsOn, reason = d.Reason, revokedAt = d.RevokedAt }).ToListAsync(ct);
        var files = await (from a in db.Attachments.AsNoTracking()
                           join r in db.FlowRequests.AsNoTracking() on a.RequestId equals r.Id
                           where a.WorkspaceId == workspaceId && a.UploadedBy == userId
                           select new { a.FileName, a.ContentType, a.Size, a.CreatedAt, requestNumber = r.Number }).ToListAsync(ct);

        JsonElement J(object o) => JsonSerializer.SerializeToElement(o, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new PersonalDataDto(clock.GetUtcNow(),
            J(new { id = u.Id, email = u.Email, displayName = u.DisplayName, role = RoleNames.ToApi(m.Role), manager = managerName, createdAt = u.CreatedAt }),
            J(requestRows), J(decisions), J(delegations), J(files), J(new { emailEnabled = !u.EmailOptOut }));
    }

    public async Task AnonymizeAsync(Guid actorId, Guid workspaceId, Guid userId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == userId && x.ManagedByWorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Kullanıcı bulunamadı.");
        if (u.AnonymizedAt is not null) throw new ConflictException("already_anonymized", "Kullanıcı zaten anonimleştirilmiş.");
        if (await db.Memberships.AnyAsync(m => m.UserId == userId, ct))
            throw new ConflictException("still_member", "Kullanıcı hâlâ bir çalışma alanının üyesi. Önce çalışma alanından çıkarın.");

        u.DisplayName = AnonymousName;
        u.Email = $"anonim-{userId:N}@anonymized.invalid";
        u.PasswordHash = $"disabled${Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}";
        u.EmailOptOut = true;
        u.MustChangePassword = false;
        u.AnonymizedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "user.anonymized", "user", userId.ToString());
        await db.SaveChangesAsync(ct);
    }
}
