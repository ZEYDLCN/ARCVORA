using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcvora.Infrastructure.Jobs;

/// <summary>
/// Kuyruktaki e-postaları gönderir. Güvenli claim (FOR UPDATE SKIP LOCKED; birden çok işçi aynı maili iki kez almaz), artan aralıkla sınırlı
/// yeniden deneme, kalıcı hatada (alıcı reddedildi) hemen <c>Failed</c>. Gönderim anında kullanıcı bildirimi kapatmışsa mail <c>Skipped</c> olur.
/// </summary>
public sealed class EmailDispatcher(IServiceScopeFactory scopes, IOptions<EmailOptions> options, TimeProvider clock, ILogger<EmailDispatcher> log)
{
    private EmailOptions Opt => options.Value;

    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
        if (!Opt.Enabled) return false;
        var id = await ClaimAsync(ct);
        if (id is null) return false;
        await DeliverAsync(id.Value, ct);
        return true;
    }

    private async Task<Guid?> ClaimAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE "EmailMessages" SET "Attempts" = "Attempts" + 1, "NextAttemptAt" = @lease
            WHERE "Id" = (
                SELECT "Id" FROM "EmailMessages"
                WHERE "Status" = 'Pending' AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= @now)
                ORDER BY "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING "Id"
            """, conn);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("lease", now.AddSeconds(Opt.LeaseSeconds));
        return await cmd.ExecuteScalarAsync(ct) is Guid g ? g : null;
    }

    private async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var m = await db.EmailMessages.FirstAsync(x => x.Id == id, ct);
        var now = clock.GetUtcNow();

        // Kullanıcı ilgili anda bildirimleri kapattıysa veya hesabı silindiyse gönderilmez.
        if (m.ToUserId is { } uid)
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => new { u.EmailOptOut, u.IsDemo }).FirstOrDefaultAsync(ct);
            if (user is null || user.EmailOptOut || user.IsDemo)
            {
                m.Status = EmailStatus.Skipped;
                m.NextAttemptAt = null;
                await db.SaveChangesAsync(ct);
                return;
            }
        }

        try
        {
            await sender.SendAsync(new OutgoingMail(m.ToAddress, m.ToName, m.Subject, m.TextBody, m.HtmlBody), ct);
            m.Status = EmailStatus.Sent;
            m.SentAt = now;
            m.LastError = null;
            m.NextAttemptAt = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var permanent = ex is EmailSendException { Permanent: true };
            // Yalnızca kısa, kimlik bilgisi içermeyen ileti saklanır.
            var error = ex is EmailSendException ? ex.Message : $"{ex.GetType().Name}";
            m.LastError = error.Length > 400 ? error[..400] : error;
            if (permanent || m.Attempts >= Opt.MaxAttempts)
            {
                m.Status = EmailStatus.Failed;
                m.NextAttemptAt = null;
                log.LogWarning("E-posta {Mail} gönderilemedi ({Attempts} deneme): {Error}", m.Id, m.Attempts, m.LastError);
            }
            else m.NextAttemptAt = now + AccountingDispatcher.Backoff(m.Attempts, Opt.RetryBaseSeconds);
        }
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Worker süreci (veya API içinde) e-posta kuyruğunu tüketir.</summary>
public sealed class EmailWorkerService(EmailDispatcher dispatcher, IOptions<EmailOptions> options, ILogger<EmailWorkerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) { log.LogInformation("E-posta bildirimleri kapalı (Email:Enabled=false)."); return; }
        log.LogInformation("E-posta işçisi başladı.");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await dispatcher.RunOnceAsync(ct)) await Task.Delay(options.Value.PollIntervalMs + Random.Shared.Next(0, 250), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "E-posta işçisi döngüsünde hata.");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
