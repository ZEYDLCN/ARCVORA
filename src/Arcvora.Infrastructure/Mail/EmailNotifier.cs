using Arcvora.Core.Notifications;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Mail;

/// <summary>
/// Olaylara bağlı bildirim e-postalarını kuyruğa (outbox) yazar. SaveChanges çağırmaz: mail, olayı oluşturan işlemle birlikte kaydedilir
/// ("olay oldu ama mail kayboldu" ya da "mail gitti ama olay geri alındı" olmaz). Kurallar: e-posta kapalıysa, çalışma alanı kapattıysa,
/// kullanıcı kapattıysa, demo veriyse veya aynı olay için zaten mail varsa yazılmaz.
/// </summary>
public sealed class EmailNotifier(ArcvoraDbContext db, IOptions<EmailOptions> options, TimeProvider clock)
{
    private EmailOptions Opt => options.Value;
    public bool Enabled => Opt.Enabled;

    // ---------------------------------------------------------------- talep olayları

    public async Task TaskAssignedAsync(FlowRequest r, AppDefinition app, string step, IEnumerable<(Guid TaskId, Guid UserId)> tasks, bool reassigned = false, CancellationToken ct = default)
    {
        if (!await RequestMailsAllowedAsync(r, app, ct)) return;
        var requester = await NameAsync(r.RequesterId, ct);
        foreach (var (taskId, userId) in tasks)
            await QueueAsync(r.WorkspaceId, userId, "task_assigned", reassigned ? $"task-assigned:{taskId}:{userId}" : $"task-assigned:{taskId}", r.Id,
                u => EmailTemplates.TaskAssigned(Ctx(u, r, app, requester), step), ct);
    }

    public async Task<bool> ReminderAsync(ApprovalTask task, FlowRequest r, AppDefinition app, int count, int daysWaiting, CancellationToken ct = default)
    {
        if (!await RequestMailsAllowedAsync(r, app, ct)) return false;
        var requester = await NameAsync(r.RequesterId, ct);
        return await QueueAsync(r.WorkspaceId, task.AssigneeId, "task_reminder", $"reminder:{task.Id}:{count}", r.Id,
            u => EmailTemplates.TaskReminder(Ctx(u, r, app, requester), task.StepName, daysWaiting), ct);
    }

    /// <param name="status"><c>approved</c>, <c>rejected</c> veya <c>cancelled</c>.</param>
    public async Task ResultAsync(FlowRequest r, AppDefinition app, string status, Guid? actorId, string? step, string? comment, CancellationToken ct = default)
    {
        if (actorId == r.RequesterId) return;                                          // kendi yaptığı işlem için mail yok
        if (!await RequestMailsAllowedAsync(r, app, ct)) return;
        var requester = await NameAsync(r.RequesterId, ct);
        var actor = actorId is { } a ? await NameAsync(a, ct) : "Sistem";
        await QueueAsync(r.WorkspaceId, r.RequesterId, $"request_{status}", $"request-{status}:{r.Id}", r.Id, u =>
        {
            var c = Ctx(u, r, app, requester);
            return status switch
            {
                "approved" => EmailTemplates.RequestApproved(c),
                "rejected" => EmailTemplates.RequestRejected(c, step ?? "", comment),
                _ => EmailTemplates.RequestCancelled(c, actor),
            };
        }, ct);
    }

    // ------------------------------------------------------------------- vekâlet / hesap

    public async Task DelegationGrantedAsync(Delegation d, CancellationToken ct = default)
    {
        var from = await NameAsync(d.FromUserId, ct);
        await QueueAsync(d.WorkspaceId, d.ToUserId, "delegation_granted", $"delegation-granted:{d.Id}", null,
            u => EmailTemplates.DelegationGranted(u.DisplayName, from, d.StartsOn, d.EndsOn, d.Reason, EmailTemplates.PageLink(Opt.PublicUrl, "inbox")), ct);
    }

    /// <summary>Vekâleti iptal eden kişi kendine mail almaz: karşı tarafa bildirilir.</summary>
    public async Task DelegationRevokedAsync(Delegation d, Guid actorId, CancellationToken ct = default)
    {
        var (recipient, other) = actorId == d.ToUserId ? (d.FromUserId, d.ToUserId) : (d.ToUserId, d.FromUserId);
        var otherName = await NameAsync(other, ct);
        var byName = await NameAsync(actorId, ct);
        await QueueAsync(d.WorkspaceId, recipient, "delegation_revoked", $"delegation-revoked:{d.Id}", null,
            u => EmailTemplates.DelegationRevoked(u.DisplayName, otherName, byName), ct);
    }

    public Task InviteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default) =>
        QueueAsync(workspaceId, userId, "user_invited", $"user-invited:{workspaceId}:{userId}", null,
            u => EmailTemplates.UserInvited(u.DisplayName, u.Email, EmailTemplates.PageLink(Opt.PublicUrl, "")), ct);

    // -------------------------------------------------------------------- çekirdek

    private async Task<bool> RequestMailsAllowedAsync(FlowRequest r, AppDefinition app, CancellationToken ct) => Opt.Enabled && !app.IsDemo && await WorkspaceEnabledAsync(r.WorkspaceId, ct);

    private async Task<bool> WorkspaceEnabledAsync(Guid workspaceId, CancellationToken ct) =>
        await db.NotificationSettings.AsNoTracking().Where(s => s.WorkspaceId == workspaceId).Select(s => (bool?)s.EmailEnabled).FirstOrDefaultAsync(ct) ?? true;

    private async Task<string> NameAsync(Guid userId, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) is { Length: > 0 } n ? Clean(n) : "(eski kullanıcı)";

    private MailContext Ctx(User recipient, FlowRequest r, AppDefinition app, string requesterName) =>
        new(Clean(recipient.DisplayName), r.Number, app.Name, requesterName, r.Summary, EmailTemplates.RequestLink(Opt.PublicUrl, r.Id));

    private async Task<bool> QueueAsync(Guid workspaceId, Guid userId, string kind, string dedupeKey, Guid? requestId, Func<User, MailContent> make, CancellationToken ct)
    {
        if (!Opt.Enabled) return false;
        if (!await WorkspaceEnabledAsync(workspaceId, ct)) return false;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.IsDemo || user.EmailOptOut || string.IsNullOrWhiteSpace(user.Email)) return false;
        if (db.EmailMessages.Local.Any(m => m.DedupeKey == dedupeKey) || await db.EmailMessages.AnyAsync(m => m.DedupeKey == dedupeKey, ct)) return false;

        var content = make(user);
        db.EmailMessages.Add(new EmailMessage
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, ToUserId = user.Id, ToAddress = user.Email, ToName = Clean(user.DisplayName), Kind = kind,
            Subject = content.Subject, TextBody = content.Text, HtmlBody = content.Html, DedupeKey = dedupeKey, RequestId = requestId,
            Status = EmailStatus.Pending, NextAttemptAt = clock.GetUtcNow(), CreatedAt = clock.GetUtcNow(),
        });
        return true;
    }

    /// <summary>Başlık/alıcı adı tek satır olmalı: denetim ve satır sonu karakterleri atılır (başlık enjeksiyonu).</summary>
    public static string Clean(string s)
    {
        var chars = s.Where(c => char.GetUnicodeCategory(c) is not (System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator)).ToArray();
        var one = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return one.Length > 200 ? one[..200] : one;
    }
}
