using System.Globalization;
using System.Text.Json;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

public sealed record AuditRowDto(long Id, DateTimeOffset At, Guid? ActorId, string? ActorName, string Action, string? ObjectType, string? ObjectId, JsonElement? Detail);
public sealed record AuditPageDto(IReadOnlyList<AuditRowDto> Items, int Total, int Page, int PageSize);

/// <summary>Denetim kaydı (master admin): kim, ne zaman, ne yaptı. Çalışma alanıyla sınırlıdır; süzgeç ve sayfalama vardır.</summary>
public sealed class AuditViewService(ArcvoraDbContext db, AppAccessService access, IOptions<LocaleOptions> locale)
{
    public async Task<AuditPageDto> ListAsync(Guid actorId, Guid workspaceId, string? action, Guid? by, string? from, string? to, string? page, string? pageSize, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var p = Int(page, 1, "page", 1, int.MaxValue);
        var size = Int(pageSize, 50, "pageSize", 1, 200);
        var offset = TimeSpan.FromMinutes(locale.Value.UtcOffsetMinutes);
        var f = Day(from, "from");
        var t = Day(to, "to");
        if (f is { } a && t is { } b && b < a) throw new RequestValidationException("invalid_range", "Bitiş tarihi başlangıçtan önce olamaz.");

        var q = db.AuditEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(action)) { var act = action.Trim(); q = q.Where(e => e.Action == act); }
        if (by is { } who) q = q.Where(e => e.ActorId == who);
        if (f is { } fd) { var start = new DateTimeOffset(fd.ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime(); q = q.Where(e => e.At >= start); }
        if (t is { } td) { var end = new DateTimeOffset(td.AddDays(1).ToDateTime(TimeOnly.MinValue), offset).ToUniversalTime(); q = q.Where(e => e.At < end); }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var ids = rows.Where(r => r.ActorId != null).Select(r => r.ActorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return new AuditPageDto(rows.Select(r => new AuditRowDto(r.Id, r.At, r.ActorId, r.ActorId is { } id ? names.GetValueOrDefault(id, "(eski kullanıcı)") : null, r.Action, r.ObjectType, r.ObjectId,
            r.DetailJson is null ? null : JsonDocument.Parse(r.DetailJson).RootElement.Clone())).ToList(), total, p, size);
    }

    private static int Int(string? s, int fallback, string name, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            throw new RequestValidationException("invalid_paging", $"{name} {min}–{(max == int.MaxValue ? "…" : max.ToString())} arasında bir sayı olmalı.");
        return v;
    }

    private static DateOnly? Day(string? s, string name)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw new RequestValidationException("invalid_date", $"{name} yyyy-AA-gg biçiminde olmalı.");
        return d;
    }
}
