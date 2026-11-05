using System.Security.Cryptography;
using System.Text;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcvora.Infrastructure.Jobs;

/// <summary>
/// Onaylanan taleplerin muhasebe webhook'una teslimi. Güvenli claim (FOR UPDATE SKIP LOCKED), imzalı (HMAC-SHA256) ve idempotent
/// (<c>Idempotency-Key</c> = kayıt kimliği) POST, artan aralıkla sınırlı yeniden deneme; sonunda <c>Failed</c>.
/// Webhook tanımlı olmayan çalışma alanlarının kayıtları <c>Pending</c> kalır ve elle işlenir.
/// </summary>
public sealed class AccountingDispatcher(IServiceScopeFactory scopes, IHttpClientFactory http, IOptions<AccountingOptions> options, TimeProvider clock, ILogger<AccountingDispatcher> log)
{
    public const string ClientName = "accounting";
    private AccountingOptions Opt => options.Value;

    public static TimeSpan Backoff(int attempts, int baseSeconds) =>
        TimeSpan.FromSeconds(Math.Min(6 * 3600d, baseSeconds * Math.Pow(2, Math.Max(0, attempts - 1))));

    public static string Sign(string secret, string body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>Bir kayıt alıp göndermeyi dener. Gönderilecek kayıt yoksa false.</summary>
    public async Task<bool> RunOnceAsync(CancellationToken ct)
    {
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
            UPDATE "AccountingEntries" SET "Attempts" = "Attempts" + 1, "NextAttemptAt" = @lease
            WHERE "Id" = (
                SELECT e."Id" FROM "AccountingEntries" e
                JOIN "AccountingSettings" s ON s."WorkspaceId" = e."WorkspaceId"
                WHERE e."Status" = 'Pending' AND s."WebhookUrl" IS NOT NULL AND (e."NextAttemptAt" IS NULL OR e."NextAttemptAt" <= @now)
                ORDER BY e."CreatedAt"
                FOR UPDATE OF e SKIP LOCKED
                LIMIT 1)
            RETURNING "Id"
            """, conn);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("lease", now.AddSeconds(Opt.LeaseSeconds));
        return await cmd.ExecuteScalarAsync(ct) is Guid g ? g : null;
    }

    private async Task DeliverAsync(Guid entryId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var entry = await db.AccountingEntries.FirstAsync(e => e.Id == entryId, ct);
        var settings = await db.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.WorkspaceId == entry.WorkspaceId, ct);
        var now = clock.GetUtcNow();

        string? error = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, settings!.WebhookUrl) { Content = new StringContent(entry.PayloadJson, Encoding.UTF8, "application/json") };
            req.Headers.Add("Idempotency-Key", entry.Id.ToString());
            req.Headers.Add("X-Arcvora-Event", "accounting.entry");
            if (!string.IsNullOrEmpty(settings.WebhookSecret)) req.Headers.Add("X-Arcvora-Signature", Sign(settings.WebhookSecret, entry.PayloadJson));
            using var res = await http.CreateClient(ClientName).SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) error = $"HTTP {(int)res.StatusCode}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Ayrıntı sızdırmamak için yalnızca tür ve kısa ileti saklanır.
            error = $"{ex.GetType().Name}: {ex.Message}";
            if (error.Length > 400) error = error[..400];
        }

        if (error is null)
        {
            entry.Status = AccountingStatus.Sent;
            entry.SentAt = now;
            entry.LastError = null;
            entry.NextAttemptAt = null;
            db.RequestEvents.Add(Event(entry, RequestEventKinds.AccountingSent, new { entryId = entry.Id, attempts = entry.Attempts }, now));
        }
        else
        {
            entry.LastError = error;
            if (entry.Attempts >= Opt.MaxAttempts)
            {
                entry.Status = AccountingStatus.Failed;
                entry.NextAttemptAt = null;
                db.RequestEvents.Add(Event(entry, RequestEventKinds.AccountingFailed, new { entryId = entry.Id, attempts = entry.Attempts, error }, now));
                log.LogWarning("Muhasebe kaydı {Entry} {Attempts} denemeden sonra başarısız: {Error}", entry.Id, entry.Attempts, error);
            }
            else entry.NextAttemptAt = now + Backoff(entry.Attempts, Opt.RetryBaseSeconds);
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            // Aynı anda muhasebe kullanıcısı kaydı işledi veya yeniden denemeye aldı: onun kararı geçerlidir.
            log.LogInformation("Muhasebe kaydı {Entry} teslim sırasında elle değiştirildi; sonuç yazılmadı.", entry.Id);
        }
    }

    private static RequestEvent Event(AccountingEntry e, string kind, object detail, DateTimeOffset at) => new()
    {
        RequestId = e.RequestId, WorkspaceId = e.WorkspaceId, At = at, Kind = kind, DetailJson = ArcvoraJson.Serialize(detail),
    };
}

/// <summary>Worker süreci (veya API içinde) muhasebe kuyruğunu tüketir.</summary>
public sealed class AccountingWorkerService(AccountingDispatcher dispatcher, IOptions<AccountingOptions> options, ILogger<AccountingWorkerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Muhasebe işçisi başladı.");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await dispatcher.RunOnceAsync(ct)) await Task.Delay(options.Value.PollIntervalMs + Random.Shared.Next(0, 250), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Muhasebe işçisi döngüsünde hata.");
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
