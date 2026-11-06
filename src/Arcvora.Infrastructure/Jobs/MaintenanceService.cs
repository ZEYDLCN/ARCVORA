using Arcvora.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Arcvora.Infrastructure.Common;

namespace Arcvora.Infrastructure.Jobs;

public sealed record MaintenanceResult(int StagedFilesPurged, int RemindersQueued = 0, int RequestsPurged = 0, int EmailsPurged = 0);

/// <summary>Düzenli bakım turu. Her iş kendi içinde idempotenttir; çökerse sonraki turda devam eder.</summary>
public sealed class MaintenanceService(IServiceScopeFactory scopes, ILogger<MaintenanceService> log)
{
    public async Task<MaintenanceResult> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var purged = 0;
        var reminders = 0;
        await Safe("hazırlanmış ek temizliği", async () => purged = await scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeStagedAsync(ct));
        await Safe("onay hatırlatmaları", async () => reminders = await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(ct));
        return new MaintenanceResult(purged, reminders);

        async Task Safe(string what, Func<Task> work)
        {
            try { await work(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { log.LogError(ex, "Bakım işi başarısız: {Work}", what); }       // bir işin hatası diğerlerini durdurmaz
        }
    }
}

public sealed class MaintenanceWorkerService(MaintenanceService maintenance, IOptions<MaintenanceOptions> options, ILogger<MaintenanceWorkerService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes));
        log.LogInformation("Bakım işçisi başladı (her {Minutes} dk).", interval.TotalMinutes);
        while (!ct.IsCancellationRequested)
        {
            try { await maintenance.RunOnceAsync(ct); await Task.Delay(interval, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Bakım işçisi döngüsünde hata.");
                try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
