using System.Net.Http;
using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcvora.Infrastructure.Jobs;

/// <summary>
/// Dayanıklı iş kuyruğu: güvenli claim (FOR UPDATE SKIP LOCKED), lease + kalp atışı, worker çökmesi sonrası
/// yeniden işleme, sınırlı yeniden deneme ve iptal. İş sonucu, lease hâlâ bizdeyse ve iptal istenmediyse
/// tek işlemde yazılır; aksi hâlde projeye hiçbir şey uygulanmaz (§15.3).
/// </summary>
public sealed class JobRunner(IServiceScopeFactory scopes, IOptions<JobOptions> options, TimeProvider clock, ILogger<JobRunner> log)
{
    private JobOptions Opt => options.Value;

    /// <summary>Bir iş alıp çalıştırır. İş bulunamazsa false.</summary>
    public async Task<bool> RunOnceAsync(string workerId, CancellationToken ct)
    {
        var jobId = await ClaimAsync(workerId, ct);
        if (jobId is null) return false;
        await ExecuteAsync(jobId.Value, workerId, ct);
        return true;
    }

    // ---------------------------------------------------------------- claim

    private async Task<Guid?> ClaimAsync(string workerId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;

        // Lease'i dolmuş ve deneme hakkı bitmiş işler başarısız sayılır (sonsuz çökme döngüsü olmaz).
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "Jobs" SET "Status" = 'Failed', "ErrorCode" = 'lease_expired',
                "Error" = 'Worker yanıt vermeyi bıraktı ve deneme hakkı doldu.', "LeaseUntil" = NULL, "UpdatedAt" = {0}
            WHERE "Status" = 'Running' AND "LeaseUntil" < {0} AND "Attempts" >= "MaxAttempts"
            """, [now], ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            UPDATE "Jobs" SET "Status" = 'Running', "LockedBy" = @worker, "LeaseUntil" = @lease,
                "Attempts" = "Attempts" + 1, "UpdatedAt" = @now, "Stage" = NULL
            WHERE "Id" = (
                SELECT "Id" FROM "Jobs"
                WHERE ("Status" = 'Queued' AND ("AvailableAt" IS NULL OR "AvailableAt" <= @now))
                   OR ("Status" = 'Running' AND "LeaseUntil" < @now)
                ORDER BY "CreatedAt"
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING "Id"
            """, conn);
        cmd.Parameters.AddWithValue("worker", workerId);
        cmd.Parameters.AddWithValue("now", now);
        cmd.Parameters.AddWithValue("lease", now.AddSeconds(Opt.LeaseSeconds));
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid id ? id : null;
    }

    // ------------------------------------------------------------ çalıştırma

    private async Task ExecuteAsync(Guid jobId, string workerId, CancellationToken stopping)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var heartbeat = HeartbeatAsync(jobId, workerId, jobCts);

        try
        {
            PendingProposal pending;
            Job snapshot;
            using (var scope = scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
                snapshot = await db.Jobs.AsNoTracking().FirstAsync(j => j.Id == jobId, jobCts.Token);
                if (snapshot.CancelRequested)
                {
                    await FinishAsync(jobId, workerId, j => { j.Status = JobStatus.Cancelled; j.Stage = null; }, null);
                    return;
                }

                var handler = scope.ServiceProvider.GetRequiredService<GenerationJobHandler>();
                pending = await handler.RunAsync(snapshot, (stage, ct) => SetStageAsync(jobId, workerId, stage, ct), jobCts.Token);
            }

            await FinishAsync(jobId, workerId, job =>
            {
                job.Status = JobStatus.Succeeded;
                job.Stage = null;
                job.LeaseUntil = null;
                job.TokensUsed = pending.TokensUsed;
            }, (scope, db, job) =>
            {
                var proposals = scope.ServiceProvider.GetRequiredService<ProposalService>();
                var entity = proposals.Stage(job.ProjectId, job.CreatedBy, pending, job.Id);
                job.ProposalId = entity.Id;
            });
        }
        catch (OperationCanceledException) when (jobCts.IsCancellationRequested)
        {
            // Kapanış, iptal veya lease kaybı. Kapanışta işi kuyruğa geri veririz; iptalde iptal ederiz.
            if (stopping.IsCancellationRequested)
                await RequeueAsync(jobId, workerId, "Worker kapandı.", immediate: true);
            else
                await FinishAsync(jobId, workerId, j => { j.Status = JobStatus.Cancelled; j.Stage = null; }, null);
        }
        catch (JobFailureException ex)
        {
            log.LogWarning("İş {Job} başarısız: {Code} {Message}", jobId, ex.Code, ex.Message);
            await FinishAsync(jobId, workerId, j =>
            {
                j.Status = JobStatus.Failed;
                j.ErrorCode = ex.Code;
                j.Error = Trim(ex.Message);
                j.Stage = ex.Stage ?? j.Stage;
                j.LeaseUntil = null;
                j.ProblemsJson = ex.Problems.Count == 0 ? null : JsonSerializer.Serialize(ex.Problems.Take(30).Select(p => Trim(p, 500)));
            }, null);
        }
        catch (Exception ex) when (ex is LlmTransientException or HttpRequestException or TimeoutException or IOException or NpgsqlException)
        {
            log.LogWarning(ex, "İş {Job} geçici hata aldı.", jobId);
            await RequeueAsync(jobId, workerId, ex.Message, immediate: false);
        }
        catch (LlmException ex)
        {
            log.LogWarning(ex, "İş {Job} sağlayıcı hatası aldı.", jobId);
            await FinishAsync(jobId, workerId, j =>
            {
                j.Status = JobStatus.Failed; j.ErrorCode = "provider_error"; j.Error = Trim(ex.Message); j.LeaseUntil = null;
            }, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "İş {Job} beklenmeyen hata aldı.", jobId);
            await RequeueAsync(jobId, workerId, "Beklenmeyen hata.", immediate: false);
        }
        finally
        {
            jobCts.Cancel();
            try { await heartbeat; } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Lease'i uzatır, iptal isteğini ve lease kaybını izler.</summary>
    private async Task HeartbeatAsync(Guid jobId, string workerId, CancellationTokenSource jobCts)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, Opt.LeaseSeconds / 3.0));
        try
        {
            while (!jobCts.IsCancellationRequested)
            {
                await Task.Delay(interval, jobCts.Token);
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
                var now = clock.GetUtcNow();
                var lease = now.AddSeconds(Opt.LeaseSeconds);

                var job = await db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => new { j.Status, j.LockedBy, j.CancelRequested }).FirstOrDefaultAsync(CancellationToken.None);
                if (job is null || job.Status != JobStatus.Running || job.LockedBy != workerId || job.CancelRequested)
                {
                    jobCts.Cancel();
                    return;
                }
                await db.Jobs.Where(j => j.Id == jobId && j.LockedBy == workerId && j.Status == JobStatus.Running)
                    .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeaseUntil, lease).SetProperty(j => j.UpdatedAt, now), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task SetStageAsync(Guid jobId, string workerId, string stage, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var now = clock.GetUtcNow();
        await db.Jobs.Where(j => j.Id == jobId && j.LockedBy == workerId && j.Status == JobStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Stage, stage).SetProperty(j => j.UpdatedAt, now), ct);
    }

    /// <summary>
    /// Sonucu yalnızca lease hâlâ bizdeyse yazar. İptal istendiyse sonuç (öneri) atılır ve iş iptal edilir.
    /// </summary>
    private async Task FinishAsync(Guid jobId, string workerId, Action<Job> mutate,
        Action<IServiceScope, ArcvoraDbContext, Job>? persist)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null || job.Status != JobStatus.Running || job.LockedBy != workerId)
        {
            log.LogWarning("İş {Job} sonucu atıldı: lease artık bu worker'da değil.", jobId);
            return;
        }

        if (job.CancelRequested && job.Status == JobStatus.Running && persist is not null)
        {
            job.Status = JobStatus.Cancelled;
            job.Stage = null;
            job.LeaseUntil = null;
            job.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        mutate(job);
        job.UpdatedAt = clock.GetUtcNow();
        if (job.Status != JobStatus.Running) job.LeaseUntil = null;
        persist?.Invoke(scope, db, job);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private async Task RequeueAsync(Guid jobId, string workerId, string error, bool immediate)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null || job.Status != JobStatus.Running || job.LockedBy != workerId) return;

        var now = clock.GetUtcNow();
        job.UpdatedAt = now;
        job.LeaseUntil = null;
        if (!immediate && job.Attempts >= job.MaxAttempts)
        {
            job.Status = JobStatus.Failed;
            job.ErrorCode = "retries_exhausted";
            job.Error = Trim($"Deneme hakkı doldu: {error}");
        }
        else
        {
            job.Status = JobStatus.Queued;
            job.LockedBy = null;
            job.Error = Trim(error);
            job.AvailableAt = immediate ? now : now.AddSeconds(Opt.RetryBaseSeconds * Math.Pow(2, Math.Max(0, job.Attempts - 1)));
            if (immediate) job.Attempts = Math.Max(0, job.Attempts - 1);   // kapanış deneme hakkı tüketmez
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Trim(string s, int max = 2000) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Worker süreci (veya geliştirmede API içinde) iş kuyruğunu tüketir.</summary>
public sealed class JobWorkerService(JobRunner runner, IOptions<JobOptions> options, ILogger<JobWorkerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var id = $"{Environment.MachineName}-{Environment.ProcessId}";
        var loops = Enumerable.Range(1, Math.Max(1, options.Value.Concurrency)).Select(n => LoopAsync($"{id}-{n}", stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task LoopAsync(string workerId, CancellationToken ct)
    {
        log.LogInformation("Worker {Worker} başladı.", workerId);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var worked = await runner.RunOnceAsync(workerId, ct);
                if (!worked) await Task.Delay(options.Value.PollIntervalMs + Random.Shared.Next(0, 250), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Worker {Worker} döngüsünde hata.", workerId);
                try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch (OperationCanceledException) { break; }
            }
        }
        log.LogInformation("Worker {Worker} durdu.", workerId);
    }
}
