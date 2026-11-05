using Arcvora.Core.Flows;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Talep onaylandığı işlemde (aynı SaveChanges) muhasebe kaydını oluşturur: onay ile kayıt ya birlikte yazılır ya hiç yazılmaz.
/// Muhasebe grubunun üyeleri, muhasebe kaydı olan talepleri görüntüleyebilir.
/// </summary>
public sealed class AccountingHooks(ArcvoraDbContext db, TimeProvider clock) : IRequestHooks
{
    public async Task OnApprovedAsync(FlowRequest request, AppDefinition app, FlowDefinition def, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        if (def.Completion?.Accounting is not { Enabled: true } acc) return;

        var requester = await db.Users.AsNoTracking().FirstAsync(u => u.Id == request.RequesterId, ct);
        // Sorgu süzgeci veritabanında çalışır; az önce verilen (henüz kaydedilmemiş) karar yalnızca izlenen varlıkta görünür.
        // Bu yüzden talebin tüm görevleri yüklenir ve durum belleğte süzülür.
        var approved = (await db.ApprovalTasks.Where(t => t.RequestId == request.Id).ToListAsync(ct)).Where(t => t.Status == ApprovalStatus.Approved).ToList();
        var userIds = approved.Where(t => t.DecidedBy != null).Select(t => t.DecidedBy!.Value)
            .Concat(def.Fields.Where(f => f.Type == FieldType.User && values.GetValueOrDefault(f.Key) is string).Select(f => Guid.Parse((string)values[f.Key]!)))
            .Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var attachments = AttachmentList.Ordered(def, values, await db.Attachments.AsNoTracking().Where(a => a.RequestId == request.Id).ToListAsync(ct));

        var entryId = Guid.NewGuid();
        var payload = new
        {
            entryId, requestId = request.Id, number = request.Number,
            app = new { id = app.Id, name = app.Name }, category = acc.Category,
            requester = new { id = requester.Id, name = requester.DisplayName, email = requester.Email },
            submittedAt = request.CreatedAt, approvedAt = request.CompletedAt, summary = request.Summary,
            fields = def.Fields.Where(f => values.ContainsKey(f.Key)).Select(f => new
            {
                key = f.Key, label = f.Label, type = f.Type.ToString().ToLowerInvariant(), value = values[f.Key],
                currency = f.Type == FieldType.Money ? f.Currency ?? "TRY" : null,
                valueLabel = f.Type == FieldType.User && values[f.Key] is string s && Guid.TryParse(s, out var uid) ? names.GetValueOrDefault(uid) : null,
            }).ToList(),
            attachments = attachments.Select(a => new { a.Id, a.FieldKey, a.FileName, a.ContentType, a.Size, a.Sha256 }).ToList(),
            approvals = approved.OrderBy(t => t.DecidedAt).Select(t => new
            {
                step = t.StepName, by = new { id = t.DecidedBy, name = t.DecidedBy is { } b ? names.GetValueOrDefault(b) : null }, at = t.DecidedAt, comment = t.Comment,
            }).ToList(),
        };

        var now = clock.GetUtcNow();
        db.AccountingEntries.Add(new AccountingEntry
        {
            Id = entryId, WorkspaceId = request.WorkspaceId, RequestId = request.Id, AppId = app.Id, Number = request.Number,
            Category = acc.Category ?? "", PayloadJson = ArcvoraJson.Serialize(payload), Status = AccountingStatus.Pending,
            NextAttemptAt = now, CreatedAt = now,
        });
        db.RequestEvents.Add(new RequestEvent
        {
            RequestId = request.Id, WorkspaceId = request.WorkspaceId, At = now, Kind = RequestEventKinds.AccountingQueued,
            DetailJson = ArcvoraJson.Serialize(new { entryId, category = acc.Category }),
        });
    }

    public async Task<bool> CanViewAsync(Guid userId, FlowRequest request, CancellationToken ct)
    {
        if (!await db.AccountingEntries.AsNoTracking().AnyAsync(e => e.RequestId == request.Id, ct)) return false;
        var groupId = await db.AccountingSettings.AsNoTracking().Where(s => s.WorkspaceId == request.WorkspaceId).Select(s => s.AccountingGroupId).FirstOrDefaultAsync(ct);
        return groupId is { } g && await db.GroupMembers.AsNoTracking().AnyAsync(gm => gm.GroupId == g && gm.UserId == userId, ct);
    }
}
