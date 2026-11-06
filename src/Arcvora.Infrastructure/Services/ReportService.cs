using System.Globalization;
using System.Text;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

public sealed record SearchPageDto(IReadOnlyList<RequestSummaryDto> Items, int Total, int Page, int PageSize);

public sealed record AppReportRow(Guid AppId, string Name, int Total, int InReview, int Approved, int Rejected, int Cancelled, double? AvgHours, double? MedianHours);
public sealed record ApproverRow(Guid UserId, string Name, int Pending, double? OldestPendingHours, int Decided);
public sealed record DailyRow(DateOnly Day, int Count);
public sealed record OverdueRow(Guid RequestId, string Number, string AppName, string Step, string Assignee, double AgeHours);
public sealed record ReportDto(DateOnly From, DateOnly To, int Total, IReadOnlyDictionary<string, int> ByStatus, IReadOnlyList<AppReportRow> Apps,
    IReadOnlyList<ApproverRow> Approvers, IReadOnlyList<DailyRow> Daily, IReadOnlyList<OverdueRow> Overdue);

/// <summary>Master admin için talep arama (sayfalı), özet rapor ve talep CSV'si. Günler çalışma alanının yerel gününe göredir (<see cref="LocaleOptions"/>).</summary>
public sealed class ReportService(ArcvoraDbContext db, AppAccessService access, RequestService requests, IOptions<LocaleOptions> locale, TimeProvider clock)
{
    private const int MaxPageSize = 100;
    private const int MaxRangeDays = 366;
    private const int MaxExportRows = 20_000;
    private const int MaxAnalysisRows = 100_000;
    private TimeSpan Offset => TimeSpan.FromMinutes(locale.Value.UtcOffsetMinutes);
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Offset).DateTime);

    public sealed record Filter(string? Q, RequestStatus? Status, Guid? AppId, Guid? RequesterId, DateOnly? From, DateOnly? To, bool IncludeDemo);

    // ================================================================== arama

    public async Task<SearchPageDto> SearchAsync(Guid actorId, Guid workspaceId, string? q, string? status, Guid? appId, Guid? requesterId, string? from, string? to,
        string? page, string? pageSize, bool includeDemo, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var p = ParseInt(page, 1, "page", 1, int.MaxValue);
        var size = ParseInt(pageSize, 25, "pageSize", 1, MaxPageSize);
        var filter = ParseFilter(q, status, appId, requesterId, from, to, includeDemo);
        var query = Apply(db.FlowRequests.AsNoTracking().Where(r => r.WorkspaceId == workspaceId), filter);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new SearchPageDto(await requests.SummariesAsync(rows, ct), total, p, size);
    }

    private static int ParseInt(string? s, int fallback, string name, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            throw new RequestValidationException("invalid_paging", $"{name} {min}–{(max == int.MaxValue ? "…" : max.ToString())} arasında bir sayı olmalı.");
        return v;
    }

    public Filter ParseFilter(string? q, string? status, Guid? appId, Guid? requesterId, string? from, string? to, bool includeDemo)
    {
        RequestStatus? st = null;
        if (!string.IsNullOrWhiteSpace(status))
            st = status.Trim().ToLowerInvariant() switch
            {
                "in_review" => RequestStatus.InReview, "approved" => RequestStatus.Approved, "rejected" => RequestStatus.Rejected, "cancelled" => RequestStatus.Cancelled,
                _ => throw new RequestValidationException("invalid_status", "Durum in_review, approved, rejected veya cancelled olmalı."),
            };
        var f = Day(from, "from");
        var t = Day(to, "to");
        if (f is { } a && t is { } b && b < a) throw new RequestValidationException("invalid_range", "Bitiş tarihi başlangıçtan önce olamaz.");
        var text = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (text is { Length: > 100 }) throw new RequestValidationException("invalid_query", "Arama metni en fazla 100 karakter olabilir.");
        return new Filter(text, st, appId, requesterId, f, t, includeDemo);
    }

    private static DateOnly? Day(string? s, string name)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw new RequestValidationException("invalid_date", $"{name} yyyy-AA-gg biçiminde olmalı.");
        return d;
    }

    /// <summary>Yerel günün başlangıcı (UTC anı). Bitiş günü dahil olsun diye üst sınır ertesi günün başlangıcıdır.</summary>
    private DateTimeOffset StartOf(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset).ToUniversalTime();

    private IQueryable<FlowRequest> Apply(IQueryable<FlowRequest> q, Filter f)
    {
        if (f.AppId is { } a) q = q.Where(r => r.AppId == a);
        if (f.RequesterId is { } u) q = q.Where(r => r.RequesterId == u);
        if (f.Status is { } s) q = q.Where(r => r.Status == s);
        if (f.From is { } from) { var start = StartOf(from); q = q.Where(r => r.CreatedAt >= start); }
        if (f.To is { } to) { var end = StartOf(to.AddDays(1)); q = q.Where(r => r.CreatedAt < end); }
        if (!f.IncludeDemo) q = q.Where(r => !r.IsDemo);
        if (f.Q is { } text)
        {
            // % _ \ düz karakterdir (joker değil): kaçışlanır.
            var like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            q = q.Where(r => EF.Functions.ILike(r.Number, like, "\\") || EF.Functions.ILike(r.Summary, like, "\\")
                || db.Users.Any(x => x.Id == r.RequesterId && EF.Functions.ILike(x.DisplayName, like, "\\")));
        }
        return q;
    }

    // ================================================================== rapor

    public async Task<ReportDto> SummaryAsync(Guid actorId, Guid workspaceId, string? from, string? to, Guid? appId, string? overdueHours, bool includeDemo, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var toDay = Day(to, "to") ?? Today;
        var fromDay = Day(from, "from") ?? toDay.AddDays(-29);
        if (toDay < fromDay) throw new RequestValidationException("invalid_range", "Bitiş tarihi başlangıçtan önce olamaz.");
        if (toDay.DayNumber - fromDay.DayNumber + 1 > MaxRangeDays) throw new RequestValidationException("invalid_range", $"Rapor aralığı en fazla {MaxRangeDays} gün olabilir.");
        var overdue = ParseInt(overdueHours, 48, "overdueHours", 0, 24 * 365);

        var filter = new Filter(null, null, appId, null, fromDay, toDay, includeDemo);
        var q = Apply(db.FlowRequests.AsNoTracking().Where(r => r.WorkspaceId == workspaceId), filter);

        var counts = await q.GroupBy(r => new { r.AppId, r.Status }).Select(g => new { g.Key.AppId, g.Key.Status, N = g.Count() }).ToListAsync(ct);
        var byStatus = new Dictionary<string, int> { ["in_review"] = 0, ["approved"] = 0, ["rejected"] = 0, ["cancelled"] = 0 };
        foreach (var c in counts) byStatus[RequestService.Status(c.Status)] += c.N;

        var durations = await q.Where(r => r.CompletedAt != null && (r.Status == RequestStatus.Approved || r.Status == RequestStatus.Rejected))
            .Select(r => new { r.AppId, r.CreatedAt, Done = r.CompletedAt!.Value }).Take(MaxAnalysisRows).ToListAsync(ct);
        var hoursByApp = durations.GroupBy(d => d.AppId).ToDictionary(g => g.Key, g => g.Select(d => (d.Done - d.CreatedAt).TotalHours).Order().ToList());

        var appIds = counts.Select(c => c.AppId).Distinct().ToList();
        var names = await db.Apps.AsNoTracking().Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var apps = appIds.Select(id =>
        {
            int N(RequestStatus s) => counts.Where(c => c.AppId == id && c.Status == s).Sum(c => c.N);
            var h = hoursByApp.GetValueOrDefault(id);
            return new AppReportRow(id, names.GetValueOrDefault(id, "(silinmiş uygulama)"), counts.Where(c => c.AppId == id).Sum(c => c.N),
                N(RequestStatus.InReview), N(RequestStatus.Approved), N(RequestStatus.Rejected), N(RequestStatus.Cancelled),
                h is { Count: > 0 } ? Math.Round(h.Average(), 2) : null, h is { Count: > 0 } ? Math.Round(Median(h), 2) : null);
        }).OrderByDescending(a => a.Total).ThenBy(a => a.Name).ToList();

        var created = await q.Select(r => r.CreatedAt).Take(MaxAnalysisRows).ToListAsync(ct);
        var daily = created.GroupBy(c => DateOnly.FromDateTime(c.ToOffset(Offset).DateTime)).OrderBy(g => g.Key).Select(g => new DailyRow(g.Key, g.Count())).ToList();

        // Onaylayıcı yükü: şu anki bekleyen görevler + dönemde verilen kararlar.
        var now = clock.GetUtcNow();
        var openTasks = db.ApprovalTasks.AsNoTracking().Where(t => t.WorkspaceId == workspaceId && t.Status == ApprovalStatus.Pending
            && db.FlowRequests.Any(r => r.Id == t.RequestId && r.Status == RequestStatus.InReview && (includeDemo || !r.IsDemo) && (appId == null || r.AppId == appId)));
        var pending = await openTasks.GroupBy(t => t.AssigneeId).Select(g => new { UserId = g.Key, N = g.Count(), Oldest = g.Min(t => t.CreatedAt) }).ToListAsync(ct);
        var start = StartOf(fromDay);
        var end = StartOf(toDay.AddDays(1));
        var decided = await db.ApprovalTasks.AsNoTracking().Where(t => t.WorkspaceId == workspaceId && t.DecidedBy != null && t.DecidedAt >= start && t.DecidedAt < end
                && (t.Status == ApprovalStatus.Approved || t.Status == ApprovalStatus.Rejected)
                && db.FlowRequests.Any(r => r.Id == t.RequestId && (includeDemo || !r.IsDemo) && (appId == null || r.AppId == appId)))
            .GroupBy(t => t.DecidedBy!.Value).Select(g => new { UserId = g.Key, N = g.Count() }).ToListAsync(ct);
        var userIds = pending.Select(x => x.UserId).Concat(decided.Select(x => x.UserId)).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var approvers = userIds.Select(id => new ApproverRow(id, userNames.GetValueOrDefault(id, "(eski kullanıcı)"),
                pending.FirstOrDefault(p => p.UserId == id)?.N ?? 0,
                pending.FirstOrDefault(p => p.UserId == id) is { } pp ? Math.Round((now - pp.Oldest).TotalHours, 1) : null,
                decided.FirstOrDefault(d => d.UserId == id)?.N ?? 0))
            .OrderByDescending(a => a.Pending).ThenByDescending(a => a.Decided).ThenBy(a => a.Name).ToList();

        var cutoff = now.AddHours(-overdue);
        var late = await (from t in openTasks.Where(t => t.CreatedAt <= cutoff)
                          join r in db.FlowRequests.AsNoTracking() on t.RequestId equals r.Id
                          join a in db.Apps.AsNoTracking() on r.AppId equals a.Id
                          orderby t.CreatedAt
                          select new { r.Id, r.Number, App = a.Name, t.StepName, t.AssigneeId, t.CreatedAt }).Take(20).ToListAsync(ct);
        var lateNames = await db.Users.AsNoTracking().Where(u => late.Select(l => l.AssigneeId).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var overdueRows = late.Select(l => new OverdueRow(l.Id, l.Number, l.App, l.StepName, lateNames.GetValueOrDefault(l.AssigneeId, "(eski kullanıcı)"), Math.Round((now - l.CreatedAt).TotalHours, 1))).ToList();

        return new ReportDto(fromDay, toDay, byStatus.Values.Sum(), byStatus, apps, approvers, daily, overdueRows);
    }

    private static double Median(List<double> sorted) => sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    // ==================================================================== CSV

    public async Task<(byte[] Data, string FileName)> ExportCsvAsync(Guid actorId, Guid workspaceId, string? q, string? status, Guid? appId, Guid? requesterId, string? from, string? to,
        bool includeDemo, string? delimiter, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var sep = delimiter switch { null or "" or ";" or "semicolon" => ';', "," or "comma" => ',', _ => throw new RequestValidationException("invalid_delimiter", "Ayırıcı ';' veya ',' olmalı.") };
        var filter = ParseFilter(q, status, appId, requesterId, from, to, includeDemo);
        var rows = await Apply(db.FlowRequests.AsNoTracking().Where(r => r.WorkspaceId == workspaceId), filter)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(MaxExportRows).ToListAsync(ct);

        var appIds = rows.Select(r => r.AppId).Distinct().ToList();
        var versionIds = rows.Select(r => r.AppVersionId).Distinct().ToList();
        var appNames = await db.Apps.AsNoTracking().Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var defs = (await db.AppVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).Select(v => new { v.Id, v.DefinitionJson }).ToListAsync(ct))
            .ToDictionary(v => v.Id, v => AppDesignService.Deserialize(v.DefinitionJson));
        var userIds = rows.Select(r => r.RequesterId).ToHashSet();
        foreach (var r in rows)
            if (defs.TryGetValue(r.AppVersionId, out var d))
                foreach (var (_, id) in FormValidator.UserFieldIds(d, FlowValues.FromJson(r.DataJson))) userIds.Add(id);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName, u.Email }).ToListAsync(ct);
        var people = users.ToDictionary(u => u.Id, u => u.DisplayName);

        var sb = new StringBuilder("﻿");
        sb.Append(Line(sep, "Numara", "Uygulama", "Durum", "Talep eden", "E-posta", "Oluşturma (UTC)", "Sonuç (UTC)", "Süre (saat)", "Özet", "Alanlar"));
        foreach (var r in rows)
        {
            var values = FlowValues.FromJson(r.DataJson);
            var fields = defs.TryGetValue(r.AppVersionId, out var def)
                ? string.Join(" | ", def.Fields.Where(f => values.ContainsKey(f.Key)).Select(f => $"{f.Label}={FlowDisplay.Format(f, values[f.Key], people)}")) : "";
            var who = users.FirstOrDefault(u => u.Id == r.RequesterId);
            sb.Append(Line(sep, r.Number, appNames.GetValueOrDefault(r.AppId, ""), Arcvora.Core.Flows.RequestText.StatusLabel(RequestService.Status(r.Status)), who?.DisplayName ?? "(eski kullanıcı)", who?.Email ?? "",
                Utc(r.CreatedAt), r.CompletedAt is { } c ? Utc(c) : "", r.CompletedAt is { } d2 ? Math.Round((d2 - r.CreatedAt).TotalHours, 2).ToString("0.##", CultureInfo.InvariantCulture) : "", r.Summary, fields));
        }
        return (Encoding.UTF8.GetBytes(sb.ToString()), $"talepler-{clock.GetUtcNow():yyyyMMdd-HHmm}.csv");
    }

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private static string Line(char sep, params string?[] cells) => string.Join(sep, cells.Select(c => AccountingService.Cell(c, sep))) + "\r\n";
}
