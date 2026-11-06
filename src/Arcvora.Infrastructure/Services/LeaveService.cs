using System.Globalization;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

public sealed record HolidayDto(Guid Id, DateOnly Date, string Name, bool HalfDay);
public sealed record AddHolidayRequest(string? Date, string? Name, bool? HalfDay);
public sealed record SeedHolidaysRequest(int? Year);
public sealed record SeedHolidaysResult(int Added, int Skipped);

public sealed record LeaveEntryDto(Guid Id, DateTimeOffset At, string Kind, decimal Days, string? Note, string? RequestNumber);
public sealed record LeaveBalanceDto(int Year, decimal Entitled, decimal Adjusted, decimal Carried, decimal Used, decimal Available, IReadOnlyList<LeaveEntryDto> Entries);
public sealed record LeaveRowDto(Guid UserId, string Name, DateOnly? HireDate, decimal Entitled, decimal Adjusted, decimal Carried, decimal Used, decimal Available);
public sealed record AddLeaveEntryRequest(Guid? UserId, int? Year, string? Kind, decimal? Days, string? Note);
public sealed record DistributeRequest(int? Year);
public sealed record DistributeResult(int Created, int SkippedExisting, int SkippedNoHireDate, int SkippedNoEntitlement);
public sealed record CarryoverRequest(int? FromYear, decimal? MaxDays);
public sealed record CarryoverResult(int Created, decimal TotalDays, int SkippedExisting);

/// <summary>
/// Resmî tatil takvimi ve izin bakiyesi. Bakiye bir defterdir (<see cref="LeaveEntry"/>): talep gönderilince gün sayısı REZERVE edilir, reddedilir/iptal
/// edilirse iade edilir. Rezerve işlemi kişi başına danışma kilidi altında yapılır; eşzamanlı iki talep aynı günleri iki kez harcayamaz.
/// </summary>
public sealed class LeaveService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, IOptions<LocaleOptions> locale, TimeProvider clock)
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    private DateOnly Today => DelegationRules.Today(clock.GetUtcNow(), locale.Value.UtcOffsetMinutes);

    // ===================================================================== tatiller

    /// <summary>Türkiye'de tarihi sabit resmî tatiller. Dinî bayramların tarihi her yıl değişir; yönetici ayrıca ekler.</summary>
    private static readonly (int Month, int Day, string Name, bool Half)[] Fixed =
    [
        (1, 1, "Yılbaşı", false), (4, 23, "Ulusal Egemenlik ve Çocuk Bayramı", false), (5, 1, "Emek ve Dayanışma Günü", false),
        (5, 19, "Atatürk'ü Anma, Gençlik ve Spor Bayramı", false), (7, 15, "Demokrasi ve Milli Birlik Günü", false), (8, 30, "Zafer Bayramı", false),
        (10, 28, "Cumhuriyet Bayramı Arifesi (yarım gün)", true), (10, 29, "Cumhuriyet Bayramı", false),
    ];

    public async Task<IReadOnlyList<HolidayDto>> HolidaysAsync(Guid userId, Guid workspaceId, string? year, CancellationToken ct)
    {
        await access.MemberAsync(userId, workspaceId, ct);
        var q = db.Holidays.AsNoTracking().Where(h => h.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(year)) { var y = Year(year, "year"); q = q.Where(h => h.Date.Year == y); }
        return await q.OrderBy(h => h.Date).Take(500).Select(h => new HolidayDto(h.Id, h.Date, h.Name, h.HalfDay)).ToListAsync(ct);
    }

    public async Task<HolidayDto> AddHolidayAsync(Guid actorId, Guid workspaceId, AddHolidayRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        if (!DateOnly.TryParseExact(req.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year is < MinYear or > MaxYear)
            throw new RequestValidationException("invalid_date", "Tarih yyyy-AA-gg biçiminde ve geçerli olmalı.");
        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 100 || name.Any(char.IsControl)) throw new RequestValidationException("invalid_name", "Ad 1–100 karakter olmalı.");
        var h = new Holiday { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Date = date, Name = name, HalfDay = req.HalfDay ?? false };
        db.Holidays.Add(h);
        audit.Add(workspaceId, null, actorId, "holiday.added", "holiday", h.Id.ToString(), new { date, name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("holiday_exists", "Bu tarih için zaten bir tatil tanımlı.");
        }
        return new HolidayDto(h.Id, h.Date, h.Name, h.HalfDay);
    }

    public async Task DeleteHolidayAsync(Guid actorId, Guid workspaceId, Guid id, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var h = await db.Holidays.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Tatil bulunamadı.");
        db.Holidays.Remove(h);
        audit.Add(workspaceId, null, actorId, "holiday.removed", "holiday", id.ToString(), new { h.Date, h.Name });
        await db.SaveChangesAsync(ct);
    }

    public async Task<SeedHolidaysResult> SeedFixedAsync(Guid actorId, Guid workspaceId, int? year, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var y = year is { } v && v is >= MinYear and <= MaxYear ? v : throw new RequestValidationException("invalid_year", $"Yıl {MinYear}–{MaxYear} arasında olmalı.");
        var existing = (await db.Holidays.Where(h => h.WorkspaceId == workspaceId && h.Date.Year == y).Select(h => h.Date).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var (m, d, name, half) in Fixed)
        {
            var date = new DateOnly(y, m, d);
            if (existing.Contains(date)) continue;
            db.Holidays.Add(new Holiday { Id = Guid.NewGuid(), WorkspaceId = workspaceId, Date = date, Name = name, HalfDay = half });
            added++;
        }
        audit.Add(workspaceId, null, actorId, "holiday.seeded", "workspace", workspaceId.ToString(), new { year = y, added });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("holiday_exists", "Aynı anda başka bir tatil eklendi; tekrar deneyin.");      // eşzamanlı ekleme
        }
        return new SeedHolidaysResult(added, Fixed.Length - added);
    }

    /// <summary>Çalışma alanının tüm tatilleri (iş günü hesabı için).</summary>
    public async Task<HolidayCalendar> CalendarAsync(Guid workspaceId, CancellationToken ct) =>
        new(await db.Holidays.AsNoTracking().Where(h => h.WorkspaceId == workspaceId).ToDictionaryAsync(h => h.Date, h => h.HalfDay, ct));

    // ===================================================================== bakiye

    public async Task<LeaveBalanceDto> MineAsync(Guid userId, Guid workspaceId, string? year, CancellationToken ct)
    {
        await access.MemberAsync(userId, workspaceId, ct);
        var y = year is null ? Today.Year : Year(year, "year");
        var entries = await db.LeaveEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.UserId == userId && e.Year == y).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(ct);
        return Summarize(y, entries);
    }

    public async Task<IReadOnlyList<LeaveRowDto>> BalancesAsync(Guid actorId, Guid workspaceId, string? year, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var y = year is null ? Today.Year : Year(year, "year");
        var members = await (from m in db.Memberships.AsNoTracking() join u in db.Users.AsNoTracking() on m.UserId equals u.Id where m.WorkspaceId == workspaceId orderby u.DisplayName
                             select new { m.UserId, u.DisplayName, m.HireDate }).ToListAsync(ct);
        var entries = (await db.LeaveEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.Year == y).ToListAsync(ct)).ToLookup(e => e.UserId);
        return members.Select(m =>
        {
            var b = Summarize(y, entries[m.UserId].ToList());
            return new LeaveRowDto(m.UserId, m.DisplayName, m.HireDate, b.Entitled, b.Adjusted, b.Carried, b.Used, b.Available);
        }).ToList();
    }

    private static LeaveBalanceDto Summarize(int year, List<LeaveEntry> e)
    {
        decimal Sum(params string[] kinds) => e.Where(x => kinds.Contains(x.Kind)).Sum(x => x.Days);
        return new LeaveBalanceDto(year, Sum(LeaveKinds.Entitlement), Sum(LeaveKinds.Adjustment), Sum(LeaveKinds.Carryover), -Sum(LeaveKinds.Usage, LeaveKinds.Refund), e.Sum(x => x.Days),
            e.Select(x => new LeaveEntryDto(x.Id, x.CreatedAt, x.Kind, x.Days, x.Note, x.RequestNumber)).ToList());
    }

    public async Task<LeaveEntryDto> AddEntryAsync(Guid actorId, Guid workspaceId, AddLeaveEntryRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var kind = req.Kind?.Trim().ToLowerInvariant();
        if (kind is not (LeaveKinds.Entitlement or LeaveKinds.Adjustment or LeaveKinds.Carryover)) throw new RequestValidationException("invalid_kind", "Tür entitlement, adjustment veya carryover olmalı.");
        if (req.Year is not { } year || year is < MinYear or > MaxYear) throw new RequestValidationException("invalid_year", $"Yıl {MinYear}–{MaxYear} arasında olmalı.");
        if (req.Days is not { } days || days == 0 || Math.Abs(days) > 366 || decimal.Round(days, 2) != days) throw new RequestValidationException("invalid_days", "Gün sayısı sıfırdan farklı, en fazla ±366 ve en çok 2 ondalık basamaklı olmalı.");
        if (kind != LeaveKinds.Adjustment && days < 0) throw new RequestValidationException("invalid_days", "Hak ve devir negatif olamaz; eksiltmek için düzeltme (adjustment) kullanın.");
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        if (note is { Length: > 200 }) throw new RequestValidationException("invalid_note", "Not en fazla 200 karakter olabilir.");
        if (req.UserId is not { } uid || !await db.Memberships.AnyAsync(m => m.WorkspaceId == workspaceId && m.UserId == uid, ct))
            throw new RequestValidationException("invalid_member", "Kişi bu çalışma alanının üyesi olmalı.");

        var e = new LeaveEntry { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = uid, Year = year, Kind = kind, Days = days, Note = note, CreatedBy = actorId, CreatedAt = clock.GetUtcNow() };
        db.LeaveEntries.Add(e);
        audit.Add(workspaceId, null, actorId, "leave.entry", "user", uid.ToString(), new { kind, days, year });
        await db.SaveChangesAsync(ct);
        return new LeaveEntryDto(e.Id, e.CreatedAt, e.Kind, e.Days, e.Note, null);
    }

    /// <summary>İşe giriş tarihi olan üyelere, o yıl için hak yoksa kıdem hakkını (4857/m.53) yazar.</summary>
    public async Task<DistributeResult> DistributeAsync(Guid actorId, Guid workspaceId, int? yearArg, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var year = yearArg is { } v && v is >= MinYear and <= MaxYear ? v : throw new RequestValidationException("invalid_year", $"Yıl {MinYear}–{MaxYear} arasında olmalı.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BitConverter.ToInt64(workspaceId.ToByteArray(), 0) ^ 0x4C45415645L})", ct);     // eşzamanlı iki dağıtım aynı hakkı iki kez yazmasın
        var members = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId).Select(m => new { m.UserId, m.HireDate }).ToListAsync(ct);
        var granted = (await db.LeaveEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.Year == year && e.Kind == LeaveKinds.Entitlement).Select(e => e.UserId).ToListAsync(ct)).ToHashSet();
        int created = 0, existing = 0, noHire = 0, none = 0;
        var now = clock.GetUtcNow();
        foreach (var m in members)
        {
            if (m.HireDate is not { } hire) { noHire++; continue; }
            if (granted.Contains(m.UserId)) { existing++; continue; }
            var days = LeaveEntitlement.ForYear(hire, year);
            if (days == 0) { none++; continue; }
            db.LeaveEntries.Add(new LeaveEntry { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = m.UserId, Year = year, Kind = LeaveKinds.Entitlement, Days = days,
                Note = $"Kıdem hakkı (4857 sayılı Kanun m.53): {year - hire.Year}. hizmet yılı", CreatedBy = actorId, CreatedAt = now });
            created++;
        }
        audit.Add(workspaceId, null, actorId, "leave.distributed", "workspace", workspaceId.ToString(), new { year, created });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new DistributeResult(created, existing, noHire, none);
    }

    /// <summary>Bir önceki yılın kalan izinlerini (isteğe bağlı üst sınırla) yeni yıla devir kaydı olarak yazar; kişi başına bir kez.</summary>
    public async Task<CarryoverResult> CarryoverAsync(Guid actorId, Guid workspaceId, CarryoverRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var from = req.FromYear is { } f && f is >= MinYear and < MaxYear ? f : throw new RequestValidationException("invalid_year", $"Yıl {MinYear}–{MaxYear - 1} arasında olmalı.");
        if (req.MaxDays is { } cap && (cap < 0 || cap > 366 || decimal.Round(cap, 2) != cap)) throw new RequestValidationException("invalid_days", "Üst sınır 0–366 arasında ve en çok 2 ondalık basamaklı olmalı.");
        var to = from + 1;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BitConverter.ToInt64(workspaceId.ToByteArray(), 0) ^ 0x4341525259L})", ct);
        var balances = await db.LeaveEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.Year == from).GroupBy(e => e.UserId).Select(g => new { UserId = g.Key, Available = g.Sum(x => x.Days) }).ToListAsync(ct);
        var already = (await db.LeaveEntries.AsNoTracking().Where(e => e.WorkspaceId == workspaceId && e.Year == to && e.Kind == LeaveKinds.Carryover).Select(e => e.UserId).ToListAsync(ct)).ToHashSet();
        var members = (await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId).Select(m => m.UserId).ToListAsync(ct)).ToHashSet();
        var now = clock.GetUtcNow();
        int created = 0, skipped = 0;
        decimal total = 0;
        foreach (var b in balances.Where(b => b.Available > 0 && members.Contains(b.UserId)))
        {
            if (already.Contains(b.UserId)) { skipped++; continue; }
            var amount = req.MaxDays is { } c ? Math.Min(b.Available, c) : b.Available;
            if (amount <= 0) continue;
            db.LeaveEntries.Add(new LeaveEntry { Id = Guid.NewGuid(), WorkspaceId = workspaceId, UserId = b.UserId, Year = to, Kind = LeaveKinds.Carryover, Days = amount, Note = $"{from} yılından devir", CreatedBy = actorId, CreatedAt = now });
            created++;
            total += amount;
        }
        audit.Add(workspaceId, null, actorId, "leave.carryover", "workspace", workspaceId.ToString(), new { from, created, total });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new CarryoverResult(created, total, skipped);
    }

    // ============================================================ talep akışı için

    /// <summary>
    /// Talep gönderilirken (açık işlem içinde): bakiyeden düşecekse kişi kilidi altında bakiyeyi denetler ve rezerve eder. Yetersizse 422.
    /// Hiçbir şey düşmüyorsa (ayar kapalı, tür sayılmıyor, gün 0) null döner.
    /// </summary>
    public async Task<LeaveReservation?> ReserveAsync(FlowRequest request, FlowDefinition def, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        var spec = def.Completion?.Leave;
        if (spec is not { Enabled: true } || spec.DaysField is null || spec.StartField is null) return null;
        if (values.GetValueOrDefault(spec.DaysField) is not decimal days || days <= 0) return null;
        if (spec.TypeField is not null && !(values.GetValueOrDefault(spec.TypeField) is string kind && (spec.CountsFor ?? []).Any(c => string.Equals(c.Trim(), kind, StringComparison.Ordinal)))) return null;
        if (values.GetValueOrDefault(spec.StartField) is not string start || !DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDay)) return null;

        var year = startDay.Year;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({BitConverter.ToInt64(request.RequesterId.ToByteArray(), 0) ^ 0x5245534552L})", ct);
        var available = await db.LeaveEntries.Where(e => e.WorkspaceId == request.WorkspaceId && e.UserId == request.RequesterId && e.Year == year).SumAsync(e => (decimal?)e.Days, ct) ?? 0m;
        if (days > available)
            throw new RequestValidationException("insufficient_leave_balance", $"{year} yılı izin bakiyeniz yetersiz: {available:0.##} gün kaldı, {days:0.##} gün isteniyor.", new { available, requested = days, year });

        db.LeaveEntries.Add(new LeaveEntry
        {
            Id = Guid.NewGuid(), WorkspaceId = request.WorkspaceId, UserId = request.RequesterId, Year = year, Kind = LeaveKinds.Usage, Days = -days,
            RequestId = request.Id, RequestNumber = request.Number, CreatedAt = clock.GetUtcNow(),
        });
        return new LeaveReservation(days, year, available - days);
    }

    /// <summary>Reddedilen/iptal edilen talebin rezerve ettiği günleri iade eder (bir kez). Çağıran kaydeder.</summary>
    public async Task<LeaveReservation?> RefundAsync(FlowRequest request, CancellationToken ct)
    {
        var entries = await db.LeaveEntries.AsNoTracking().Where(e => e.RequestId == request.Id).ToListAsync(ct);
        var net = entries.Sum(e => e.Days);                                                // kullanım eksi, iade artı
        if (net >= 0) return null;
        var year = entries.First().Year;
        db.LeaveEntries.Add(new LeaveEntry
        {
            Id = Guid.NewGuid(), WorkspaceId = request.WorkspaceId, UserId = request.RequesterId, Year = year, Kind = LeaveKinds.Refund, Days = -net,
            RequestId = request.Id, RequestNumber = request.Number, CreatedAt = clock.GetUtcNow(),
        });
        return new LeaveReservation(-net, year, 0);
    }

    private static int Year(string s, string name) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= MinYear and <= MaxYear ? y : throw new RequestValidationException("invalid_year", $"{name} {MinYear}–{MaxYear} arasında olmalı.");
}

public sealed record LeaveReservation(decimal Days, int Year, decimal BalanceAfter);
