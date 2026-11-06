using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Mail;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Uzun süredir bekleyen onay görevleri için hatırlatma maili: ilk hatırlatma <c>ReminderAfterHours</c> sonra, sonrakiler <c>ReminderRepeatHours</c> aralıkla,
/// en çok <c>MaxReminders</c> kez. Her hatırlatma koşullu güncellemeyle "sahiplenilir" (birden çok işçi çalışsa da tek mail); karar verilmiş görev,
/// kapanmış talep, demo uygulaması ve hatırlatması kapalı çalışma alanı atlanır.
/// </summary>
public sealed class ReminderService(ArcvoraDbContext db, EmailNotifier notifier, IOptions<EmailOptions> options, TimeProvider clock)
{
    private const int Batch = 100;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!o.Enabled || o.MaxReminders <= 0) return 0;
        var now = clock.GetUtcNow();
        var first = now.AddHours(-o.ReminderAfterHours);
        var again = now.AddHours(-o.ReminderRepeatHours);
        var disabled = db.NotificationSettings.Where(s => !s.RemindersEnabled || !s.EmailEnabled).Select(s => s.WorkspaceId);

        var due = await db.ApprovalTasks.AsNoTracking()
            .Where(t => t.Status == ApprovalStatus.Pending && t.ReminderCount < o.MaxReminders
                && ((t.ReminderCount == 0 && t.CreatedAt <= first) || (t.ReminderCount > 0 && t.RemindedAt <= again))
                && !disabled.Contains(t.WorkspaceId)
                && db.FlowRequests.Any(r => r.Id == t.RequestId && r.Status == RequestStatus.InReview))
            .OrderBy(t => t.CreatedAt).Take(Batch).ToListAsync(ct);

        var queued = 0;
        foreach (var t in due)
        {
            // Sahiplen: sayaç hâlâ eski değerdeyse ve görev bekliyorsa artır (aksi halde başka işçi aldı veya karar verildi).
            var claimed = await db.ApprovalTasks.Where(x => x.Id == t.Id && x.ReminderCount == t.ReminderCount && x.Status == ApprovalStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReminderCount, t.ReminderCount + 1).SetProperty(x => x.RemindedAt, now), ct);
            if (claimed == 0) continue;
            var request = await db.FlowRequests.AsNoTracking().FirstAsync(r => r.Id == t.RequestId, ct);
            var app = await db.Apps.AsNoTracking().FirstAsync(a => a.Id == request.AppId, ct);
            var days = Math.Max(0, (int)(now - t.CreatedAt).TotalDays);
            if (await notifier.ReminderAsync(t, request, app, t.ReminderCount + 1, days, ct)) queued++;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
        return queued;
    }
}
