using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Services;

public sealed record DelegationDto(Guid Id, Guid FromUserId, string FromName, Guid ToUserId, string ToName, DateOnly StartsOn, DateOnly EndsOn,
    string? Reason, string Status, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);

public sealed record CreateDelegationRequest(Guid? FromUserId, Guid? ToUserId, string? StartsOn, string? EndsOn, string? Reason);

public sealed record ReassignRequest(Guid? ToUserId);

/// <summary>Vekâlet (yetki devri): kişi kendi adına, master admin başkası adına oluşturur; kurallar çekirdekte (<see cref="DelegationRules"/>).</summary>
public sealed class DelegationService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, IOptions<LocaleOptions> locale, TimeProvider clock)
{
    private const int ListLimit = 200;

    public DateOnly Today => DelegationRules.Today(clock.GetUtcNow(), locale.Value.UtcOffsetMinutes);

    // ================================================================ oluşturma

    public async Task<DelegationDto> CreateAsync(Guid actorId, Guid workspaceId, CreateDelegationRequest req, CancellationToken ct)
    {
        var actor = await access.MemberAsync(actorId, workspaceId, ct);
        var from = req.FromUserId ?? actorId;
        if (from != actorId && actor.Role != WorkspaceRole.Admin) throw new ForbiddenException("Başkası adına vekâlet yalnızca master admin tarafından verilebilir.");

        var issues = new List<FlowIssue>();
        if (req.ToUserId is null || req.ToUserId == Guid.Empty) issues.Add(new FlowIssue("delegate_required", "toUserId", "Vekil seçilmeli."));
        if (!TryDay(req.StartsOn, out var start)) issues.Add(new FlowIssue("invalid_date", "startsOn", "Başlangıç tarihi yyyy-AA-gg biçiminde olmalı."));
        if (!TryDay(req.EndsOn, out var end)) issues.Add(new FlowIssue("invalid_date", "endsOn", "Bitiş tarihi yyyy-AA-gg biçiminde olmalı."));
        if (issues.Count == 0) issues.AddRange(DelegationRules.Validate(start, end, Today, req.Reason));
        if (issues.Count > 0) throw new RequestValidationException("invalid_request", "Vekâlet geçersiz.", issues);

        var to = req.ToUserId!.Value;
        if (to == from) throw new RequestValidationException("invalid_delegate", "Kişi kendine vekâlet veremez.");
        var members = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId && (m.UserId == from || m.UserId == to)).Select(m => m.UserId).ToListAsync(ct);
        if (!members.Contains(to) || !members.Contains(from)) throw new RequestValidationException("invalid_delegate", "Vekil ve vekâlet veren çalışma alanının üyesi olmalı.");

        var now = clock.GetUtcNow();
        var d = new Delegation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, FromUserId = from, ToUserId = to, StartsOn = start, EndsOn = end,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(), CreatedBy = actorId, CreatedAt = now,
        };

        // Aynı kişi için eşzamanlı iki oluşturma çakışma denetimini birlikte geçmesin: kişi başına danışma kilidi.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = BitConverter.ToInt64(from.ToByteArray(), 0);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
        if (await db.Delegations.AnyAsync(x => x.WorkspaceId == workspaceId && x.FromUserId == from && x.RevokedAt == null && x.StartsOn <= end && x.EndsOn >= start, ct))
            throw new ConflictException("delegation_overlap", "Bu kişinin aynı günlerde başka bir vekâleti var. Önce onu iptal edin veya günleri değiştirin.");
        db.Delegations.Add(d);
        audit.Add(workspaceId, null, actorId, "delegation.created", "user", from.ToString(), new { to, start, end });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ToDtosAsync([d], ct))[0];
    }

    private static bool TryDay(string? s, out DateOnly day) =>
        DateOnly.TryParseExact(s?.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out day);

    // ================================================================== listeleme

    /// <param name="scope"><c>mine</c> (varsayılan): verdiklerim ve bana verilenler. <c>all</c>: yalnızca master admin, çalışma alanındaki hepsi.</param>
    public async Task<IReadOnlyList<DelegationDto>> ListAsync(Guid actorId, Guid workspaceId, string? scope, CancellationToken ct)
    {
        var member = await access.MemberAsync(actorId, workspaceId, ct);
        var q = db.Delegations.AsNoTracking().Where(d => d.WorkspaceId == workspaceId);
        switch ((scope ?? "mine").ToLowerInvariant())
        {
            case "mine": q = q.Where(d => d.FromUserId == actorId || d.ToUserId == actorId); break;
            case "all":
                if (member.Role != WorkspaceRole.Admin) throw new ForbiddenException("Tüm vekâletleri yalnızca master admin görür.");
                break;
            default: throw new RequestValidationException("invalid_scope", "Kapsam 'mine' veya 'all' olmalı.");
        }
        var rows = await q.OrderByDescending(d => d.StartsOn).ThenByDescending(d => d.CreatedAt).Take(ListLimit).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    // ===================================================================== iptal

    /// <summary>Vekâlet veren, vekil (geri verir) veya master admin iptal eder. İlgisiz kişi için kayıt yokmuş gibi 404.</summary>
    public async Task<DelegationDto> RevokeAsync(Guid actorId, Guid workspaceId, Guid id, CancellationToken ct)
    {
        var member = await access.MemberAsync(actorId, workspaceId, ct);
        var d = await db.Delegations.FirstOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, ct) ?? throw new NotFoundException("Vekâlet bulunamadı.");
        if (member.Role != WorkspaceRole.Admin && d.FromUserId != actorId && d.ToUserId != actorId) throw new NotFoundException("Vekâlet bulunamadı.");
        if (d.RevokedAt is not null) throw new ConflictException("already_revoked", "Vekâlet zaten iptal edilmiş.");
        d.RevokedAt = clock.GetUtcNow();
        d.RevokedBy = actorId;
        audit.Add(workspaceId, null, actorId, "delegation.revoked", "user", d.FromUserId.ToString(), new { to = d.ToUserId });
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([d], ct))[0];
    }

    // ============================================================= diğer hizmetler için

    /// <summary><paramref name="delegateId"/> kişisine şu an geçerli vekâlet vermiş olan kişiler (çalışma alanı bazında).</summary>
    public IQueryable<Delegation> ActiveFor(Guid delegateId)
    {
        var today = Today;
        return db.Delegations.Where(d => d.ToUserId == delegateId && d.RevokedAt == null && d.StartsOn <= today && d.EndsOn >= today);
    }

    public Task<bool> IsActiveAsync(Guid fromUserId, Guid toUserId, Guid workspaceId, CancellationToken ct) =>
        ActiveFor(toUserId).AnyAsync(d => d.FromUserId == fromUserId && d.WorkspaceId == workspaceId, ct);

    /// <summary>Verilen kişilerden herhangi biri adına <paramref name="delegateId"/> şu an vekil mi?</summary>
    public async Task<bool> CoversAnyAsync(Guid delegateId, Guid workspaceId, IReadOnlyCollection<Guid> fromUserIds, CancellationToken ct) =>
        fromUserIds.Count > 0 && await ActiveFor(delegateId).AnyAsync(d => d.WorkspaceId == workspaceId && fromUserIds.Contains(d.FromUserId), ct);

    /// <summary>Çalışma alanından çıkan kişinin açık vekâletleri (verdiği ve aldığı) iptal edilir. Çağıran kaydeder.</summary>
    public async Task RevokeAllOfAsync(Guid workspaceId, Guid userId, Guid actorId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var d in await db.Delegations.Where(x => x.WorkspaceId == workspaceId && x.RevokedAt == null && (x.FromUserId == userId || x.ToUserId == userId)).ToListAsync(ct))
        {
            d.RevokedAt = now;
            d.RevokedBy = actorId;
        }
    }

    private async Task<IReadOnlyList<DelegationDto>> ToDtosAsync(IReadOnlyList<Delegation> rows, CancellationToken ct)
    {
        var ids = rows.SelectMany(d => new[] { d.FromUserId, d.ToUserId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var today = Today;
        return rows.Select(d => new DelegationDto(d.Id, d.FromUserId, names.GetValueOrDefault(d.FromUserId, "(eski kullanıcı)"), d.ToUserId,
            names.GetValueOrDefault(d.ToUserId, "(eski kullanıcı)"), d.StartsOn, d.EndsOn, d.Reason,
            DelegationRules.StatusOf(d.StartsOn, d.EndsOn, d.RevokedAt is not null, today).ToString().ToLowerInvariant(), d.CreatedAt, d.RevokedAt)).ToList();
    }
}
