using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

public sealed record EmailStatusDto(bool Configured, bool EmailEnabled, bool RemindersEnabled, int Pending, int Failed, string? From);
public sealed record UpdateEmailSettingsRequest(bool? EmailEnabled, bool? RemindersEnabled);
public sealed record EmailOutboxDto(Guid Id, string To, string Kind, string Subject, string Status, int Attempts, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
public sealed record TestMailResult(bool Sent, string To);
public sealed record NotificationPrefsDto(bool EmailEnabled);
public sealed record UpdateNotificationPrefsRequest(bool? EmailEnabled);

/// <summary>E-posta yönetimi (master admin): durum, çalışma alanı ayarları, test maili, kuyruk ve yeniden deneme; kullanıcı tercihi.</summary>
public sealed class EmailAdminService(ArcvoraDbContext db, AppAccessService access, IEmailSender sender, IOptions<EmailOptions> options, AuditLog audit, TimeProvider clock)
{
    private const int ListLimit = 100;

    public async Task<EmailStatusDto> StatusAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        return await BuildAsync(workspaceId, ct);
    }

    public async Task<EmailStatusDto> UpdateAsync(Guid actorId, Guid workspaceId, UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var s = await db.NotificationSettings.FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        if (s is null) db.NotificationSettings.Add(s = new NotificationSettings { WorkspaceId = workspaceId });
        if (req.EmailEnabled is { } e) s.EmailEnabled = e;
        if (req.RemindersEnabled is { } r) s.RemindersEnabled = r;
        s.UpdatedAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "email.settings", "workspace", workspaceId.ToString(), new { s.EmailEnabled, s.RemindersEnabled });
        await db.SaveChangesAsync(ct);
        return await BuildAsync(workspaceId, ct);
    }

    private async Task<EmailStatusDto> BuildAsync(Guid workspaceId, CancellationToken ct)
    {
        var s = await db.NotificationSettings.AsNoTracking().FirstOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct);
        var pending = await db.EmailMessages.CountAsync(m => m.WorkspaceId == workspaceId && m.Status == EmailStatus.Pending, ct);
        var failed = await db.EmailMessages.CountAsync(m => m.WorkspaceId == workspaceId && m.Status == EmailStatus.Failed, ct);
        return new EmailStatusDto(options.Value.Enabled, s?.EmailEnabled ?? true, s?.RemindersEnabled ?? true, pending, failed, options.Value.Enabled ? options.Value.From : null);
    }

    /// <summary>Master admin'in kendi adresine eşzamanlı test maili: ayarların çalıştığını görmek için. Hata ayrıntısı kimlik bilgisi içermez.</summary>
    public async Task<TestMailResult> SendTestAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        if (!options.Value.Enabled) throw new ConflictException("email_disabled", "E-posta yapılandırılmamış (Email__Enabled, Email__Host ve Email__From ayarlarını verin).");
        var admin = await db.Users.AsNoTracking().FirstAsync(u => u.Id == actorId, ct);
        var content = Arcvora.Core.Notifications.EmailTemplates.Test(EmailNotifier.Clean(admin.DisplayName));
        try { await sender.SendAsync(new OutgoingMail(admin.Email, EmailNotifier.Clean(admin.DisplayName), content.Subject, content.Text, content.Html), ct); }
        catch (OperationCanceledException) { throw; }
        catch (EmailSendException ex) { throw new AppException(502, "email_failed", ex.Message); }
        catch (Exception ex) { throw new AppException(502, "email_failed", $"E-posta gönderilemedi ({ex.GetType().Name})."); }
        return new TestMailResult(true, admin.Email);
    }

    public async Task<IReadOnlyList<EmailOutboxDto>> OutboxAsync(Guid actorId, Guid workspaceId, string? status, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var q = db.EmailMessages.AsNoTracking().Where(m => m.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<EmailStatus>(status, true, out var st)) throw new RequestValidationException("invalid_status", "Durum pending, sent, failed veya skipped olmalı.");
            q = q.Where(m => m.Status == st);
        }
        var rows = await q.OrderByDescending(m => m.CreatedAt).Take(ListLimit).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<EmailOutboxDto> RetryAsync(Guid actorId, Guid workspaceId, Guid mailId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var m = await db.EmailMessages.FirstOrDefaultAsync(x => x.Id == mailId && x.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("E-posta bulunamadı.");
        if (m.Status is not (EmailStatus.Failed or EmailStatus.Pending)) throw new ConflictException("not_retryable", "Yalnızca başarısız veya bekleyen e-posta yeniden denenebilir.");
        m.Status = EmailStatus.Pending;
        m.Attempts = 0;
        m.LastError = null;
        m.NextAttemptAt = clock.GetUtcNow();
        audit.Add(workspaceId, null, actorId, "email.retry", "email", mailId.ToString());
        await db.SaveChangesAsync(ct);
        return ToDto(m);
    }

    // ------------------------------------------------------------ kullanıcı tercihi

    public async Task<NotificationPrefsDto> GetPrefsAsync(Guid userId, CancellationToken ct) =>
        new(!await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.EmailOptOut).FirstAsync(ct));

    public async Task<NotificationPrefsDto> SetPrefsAsync(Guid userId, UpdateNotificationPrefsRequest req, CancellationToken ct)
    {
        if (req.EmailEnabled is not { } enabled) throw new RequestValidationException("invalid_request", "emailEnabled (true/false) gerekli.");
        var u = await db.Users.FirstAsync(x => x.Id == userId, ct);
        u.EmailOptOut = !enabled;
        await db.SaveChangesAsync(ct);
        return new NotificationPrefsDto(enabled);
    }

    private static EmailOutboxDto ToDto(EmailMessage m) => new(m.Id, m.ToAddress, m.Kind, m.Subject, m.Status.ToString().ToLowerInvariant(), m.Attempts, m.LastError, m.CreatedAt, m.SentAt);
}
