using System.Text.Json;
using Arcvora.Core.Flows;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

/// <summary>Akış uygulamalarının çalışma zamanı: talep açma, onay/ret, iptal, listeleme. Kurallar çekirdekte (<see cref="FlowEngine"/>).</summary>
public sealed class RequestService(ArcvoraDbContext db, AppAccessService access, IRequestHooks hooks, RequestVisibility visibility, TimeProvider clock)
{
    public const int MaxCommentLength = 1000;
    private const int ListLimit = 200;
    private const int MaxDecisionAttempts = 3;

    // =============================================================== uygulamalarım

    public async Task<IReadOnlyList<MyAppDto>> MyAppsAsync(Guid userId, CancellationToken ct)
    {
        var members = await db.Memberships.AsNoTracking().Where(m => m.UserId == userId).ToDictionaryAsync(m => m.WorkspaceId, ct);
        if (members.Count == 0) return [];
        var wsIds = members.Keys.ToList();
        var rows = await (from a in db.Apps.AsNoTracking()
                          join v in db.AppVersions.AsNoTracking() on a.PublishedVersionId equals v.Id
                          where wsIds.Contains(a.WorkspaceId) && a.Status == AppStatus.Published
                          select new { App = a, v.Number }).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var groupIds = db.GroupMembers.Where(gm => gm.UserId == userId).Select(gm => gm.GroupId);
        var appIds = rows.Select(r => r.App.Id).ToList();
        var granted = (await db.AppGrants.AsNoTracking().Where(g => appIds.Contains(g.AppId)
                && ((g.Kind == GrantKinds.User && g.SubjectId == userId) || (g.Kind == GrantKinds.Group && groupIds.Contains(g.SubjectId))))
            .Select(g => g.AppId).ToListAsync(ct)).ToHashSet();

        return rows.Where(r => members[r.App.WorkspaceId].Role == WorkspaceRole.Admin || granted.Contains(r.App.Id))
            .OrderBy(r => r.App.Name).Select(r => ToMy(r.App, r.Number)).ToList();
    }

    public async Task<AppFormDto> FormAsync(Guid userId, Guid appId, CancellationToken ct)
    {
        var (app, member) = await access.LoadAppAsync(userId, appId, false, ct);
        await RequireUsableAsync(userId, member, app, ct);
        var version = await db.AppVersions.AsNoTracking().FirstAsync(v => v.Id == app.PublishedVersionId, ct);
        return new AppFormDto(ToMy(app, version.Number), AppDesignService.Deserialize(version.DefinitionJson));
    }

    public async Task<IReadOnlyList<DirectoryEntry>> DirectoryAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        await access.MemberAsync(userId, workspaceId, ct);
        return await (from m in db.Memberships.AsNoTracking()
                      join u in db.Users.AsNoTracking() on m.UserId equals u.Id
                      where m.WorkspaceId == workspaceId
                      orderby u.DisplayName
                      select new DirectoryEntry(u.Id, u.DisplayName)).ToListAsync(ct);
    }

    // ================================================================== talep açma

    public async Task<(RequestDetailDto Detail, bool Created)> SubmitAsync(Guid userId, Guid appId, IReadOnlyDictionary<string, JsonElement>? data,
        string? idempotencyKey, CancellationToken ct)
    {
        var (app, member) = await access.LoadAppAsync(userId, appId, false, ct);
        await RequireUsableAsync(userId, member, app, ct);

        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is { Length: > 100 }) throw new BadRequestException("Idempotency-Key en fazla 100 karakter olabilir.");
        if (key is not null && await FindByKeyAsync(userId, key, ct) is { } existing)
            return (await GetAsync(userId, existing.Id, ct), false);

        var version = await db.AppVersions.AsNoTracking().FirstAsync(v => v.Id == app.PublishedVersionId, ct);
        var def = AppDesignService.Deserialize(version.DefinitionJson);

        var form = FormValidator.Validate(def, data ?? new Dictionary<string, JsonElement>());
        var issues = form.Issues.ToList();
        var userFields = FormValidator.UserFieldIds(def, form.Values);
        if (userFields.Count > 0)
        {
            var ids = userFields.Select(u => u.Id).Distinct().ToList();
            var valid = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == app.WorkspaceId && ids.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);
            issues.AddRange(userFields.Where(u => !valid.Contains(u.Id))
                .Select(u => new FlowIssue("invalid_user", $"data.{u.Key}", "Seçilen kişi bu çalışma alanında yok.")));
        }
        if (issues.Count > 0) throw new RequestValidationException("invalid_request", "Form geçersiz.", issues);

        var now = clock.GetUtcNow();
        var dir = await FlowDirectory.LoadAsync(db, app.WorkspaceId, def, ct);
        var request = new FlowRequest
        {
            Id = Guid.NewGuid(), WorkspaceId = app.WorkspaceId, AppId = app.Id, AppVersionId = version.Id, RequesterId = userId,
            Status = RequestStatus.InReview, DataJson = FlowValues.ToJson(form.Values), Summary = FlowValues.Summary(def, form.Values),
            IdempotencyKey = key, CreatedAt = now, UpdatedAt = now,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        request.Number = FlowValues.Number(def.Prefix, now.Year, await NextNumberAsync(app.WorkspaceId, def.Prefix, now.Year, ct));
        db.FlowRequests.Add(request);
        AddEvent(request, userId, RequestEventKinds.Submitted, new { app = app.Name, version = version.Number }, now);
        await AdvanceAsync(request, app, def, form.Values, new Requester(userId, member.ManagerId), dir, 0, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (key is not null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anahtarla eşzamanlı ikinci istek kazandı: onun talebini döndür.
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(userId, key, ct);
            if (winner is null) throw;
            return (await GetAsync(userId, winner.Id, ct), false);
        }
        return (await GetAsync(userId, request.Id, ct), true);
    }

    // ============================================================= liste ve ayrıntı

    public async Task<IReadOnlyList<RequestSummaryDto>> MyRequestsAsync(Guid userId, string? status, CancellationToken ct)
    {
        var wsIds = db.Memberships.Where(m => m.UserId == userId).Select(m => m.WorkspaceId);
        var q = db.FlowRequests.AsNoTracking().Where(r => r.RequesterId == userId && wsIds.Contains(r.WorkspaceId));
        return await ListAsync(Filter(q, status), ct);
    }

    public async Task<IReadOnlyList<RequestSummaryDto>> AllRequestsAsync(Guid actorId, Guid workspaceId, string? status, Guid? appId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var q = db.FlowRequests.AsNoTracking().Where(r => r.WorkspaceId == workspaceId);
        if (appId is { } a) q = q.Where(r => r.AppId == a);
        return await ListAsync(Filter(q, status), ct);
    }

    public async Task<RequestDetailDto> GetAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        var (r, member, tasks) = await visibility.LoadAsync(userId, requestId, ct);

        var app = await db.Apps.AsNoTracking().FirstAsync(a => a.Id == r.AppId, ct);
        var version = await db.AppVersions.AsNoTracking().FirstAsync(v => v.Id == r.AppVersionId, ct);
        var events = await db.RequestEvents.AsNoTracking().Where(e => e.RequestId == r.Id).OrderBy(e => e.Id).ToListAsync(ct);

        var ids = new HashSet<Guid> { r.RequesterId };
        foreach (var t in tasks) { ids.Add(t.AssigneeId); if (t.DecidedBy is { } d) ids.Add(d); }
        foreach (var e in events) { if (e.ActorId is { } a) ids.Add(a); }
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        // Çalışma alanından çıkmış veya silinmiş kişiler için boş ad yerine açık bir etiket.
        foreach (var id in ids.Where(i => !people.ContainsKey(i))) people[id] = "(eski kullanıcı)";

        var step = tasks.FirstOrDefault(t => t.Status == ApprovalStatus.Pending)?.StepName;
        var summary = ToSummary(r, app, people[r.RequesterId], r.Status == RequestStatus.InReview ? step : null);
        var myPending = r.Status == RequestStatus.InReview
            ? tasks.FirstOrDefault(t => t.AssigneeId == userId && t.Status == ApprovalStatus.Pending)?.Id : null;
        var canCancel = r.Status == RequestStatus.InReview && (r.RequesterId == userId || member.Role == WorkspaceRole.Admin);

        return new RequestDetailDto(summary, AppDesignService.Deserialize(version.DefinitionJson), version.Number,
            JsonDocument.Parse(r.DataJson).RootElement.Clone(),
            tasks.Select(t => new TaskDto(t.Id, t.AssigneeId, t.StepIndex, t.StepId, t.StepName, Status(t.Status), t.Comment, t.Fallback, t.CreatedAt, t.DecidedAt, t.DecidedBy)).ToList(),
            events.Select(e => new EventDto(e.Id, e.At, e.ActorId, e.Kind, e.DetailJson is null ? null : JsonDocument.Parse(e.DetailJson).RootElement.Clone())).ToList(),
            people, canCancel, myPending);
    }

    // =================================================================== iptal

    public async Task<RequestDetailDto> CancelAsync(Guid userId, Guid requestId, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var r = await db.FlowRequests.FirstOrDefaultAsync(x => x.Id == requestId, ct) ?? throw new NotFoundException("Talep bulunamadı.");
            var member = await access.MemberAsync(userId, r.WorkspaceId, ct);
            if (r.RequesterId != userId && member.Role != WorkspaceRole.Admin) throw new NotFoundException("Talep bulunamadı.");
            if (r.Status != RequestStatus.InReview) throw new ConflictException("request_closed", "Talep zaten sonuçlanmış; iptal edilemez.");

            var now = clock.GetUtcNow();
            foreach (var t in await db.ApprovalTasks.Where(t => t.RequestId == r.Id && t.Status == ApprovalStatus.Pending).ToListAsync(ct))
                t.Status = ApprovalStatus.Skipped;
            r.Status = RequestStatus.Cancelled;
            r.CompletedAt = now;
            r.UpdatedAt = now;
            AddEvent(r, userId, RequestEventKinds.Cancelled, null, now);
            try
            {
                await db.SaveChangesAsync(ct);
                return await GetAsync(userId, requestId, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxDecisionAttempts) { db.ChangeTracker.Clear(); }
            catch (DbUpdateConcurrencyException) { throw new ConflictException("concurrent_update", "Talep aynı anda değiştirildi. Tekrar deneyin."); }
        }
    }

    // ============================================================== onay görevleri

    public async Task<IReadOnlyList<MyTaskDto>> MyTasksAsync(Guid userId, bool done, CancellationToken ct)
    {
        var wsIds = db.Memberships.Where(m => m.UserId == userId).Select(m => m.WorkspaceId);
        var q = from t in db.ApprovalTasks.AsNoTracking()
                join r in db.FlowRequests.AsNoTracking() on t.RequestId equals r.Id
                join a in db.Apps.AsNoTracking() on r.AppId equals a.Id
                join u in db.Users.AsNoTracking() on r.RequesterId equals u.Id
                where t.AssigneeId == userId && wsIds.Contains(t.WorkspaceId)
                select new { Task = t, Request = r, App = a, Requester = u.DisplayName };
        q = done
            ? q.Where(x => x.Task.Status == ApprovalStatus.Approved || x.Task.Status == ApprovalStatus.Rejected).OrderByDescending(x => x.Task.DecidedAt)
            : q.Where(x => x.Task.Status == ApprovalStatus.Pending && x.Request.Status == RequestStatus.InReview).OrderBy(x => x.Task.CreatedAt);
        var rows = await q.Take(ListLimit).ToListAsync(ct);
        return rows.Select(x => new MyTaskDto(x.Task.Id, x.Request.Id, x.Request.Number, x.App.Name, x.App.Icon, x.Request.Summary, x.Requester,
            x.Task.StepName, Status(x.Task.Status), x.Task.Fallback, x.Task.Comment, x.Task.CreatedAt, x.Task.DecidedAt)).ToList();
    }

    public async Task<RequestDetailDto> DecideAsync(Guid userId, Guid taskId, ApprovalDecision decision, string? comment, CancellationToken ct)
    {
        var text = comment?.Trim();
        if (text is { Length: > MaxCommentLength }) throw new RequestValidationException("comment_too_long", $"Yorum en fazla {MaxCommentLength} karakter olabilir.");
        if (decision == ApprovalDecision.Reject && string.IsNullOrEmpty(text)) throw new RequestValidationException("comment_required", "Reddetmek için gerekçe yazın.");

        for (var attempt = 1; ; attempt++)
        {
            // Atanan kişi dışındaki herkes için görev yokmuş gibi davranılır (404).
            var task = await db.ApprovalTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.AssigneeId == userId, ct) ?? throw new NotFoundException("Görev bulunamadı.");
            var request = await db.FlowRequests.FirstAsync(r => r.Id == task.RequestId, ct);
            var member = await access.MemberAsync(userId, request.WorkspaceId, ct);
            if (request.Status != RequestStatus.InReview) throw new ConflictException("request_closed", "Talep zaten sonuçlanmış.");
            if (task.Status != ApprovalStatus.Pending) throw new ConflictException("task_not_pending", "Bu görev için karar zaten verilmiş.");

            var app = await db.Apps.FirstAsync(a => a.Id == request.AppId, ct);
            var version = await db.AppVersions.AsNoTracking().FirstAsync(v => v.Id == request.AppVersionId, ct);
            var def = AppDesignService.Deserialize(version.DefinitionJson);
            var siblings = await db.ApprovalTasks.Where(t => t.RequestId == request.Id && t.StepIndex == task.StepIndex).ToListAsync(ct);

            var now = clock.GetUtcNow();
            var result = StepEvaluator.Decide(task.Mode, siblings.Select(s => new TaskState(s.Id, s.AssigneeId, s.Status)).ToList(), task.Id, decision);

            task.Status = decision == ApprovalDecision.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
            task.Comment = text;
            task.DecidedBy = userId;
            task.DecidedAt = now;
            foreach (var s in siblings.Where(s => result.TasksToSkip.Contains(s.Id))) s.Status = ApprovalStatus.Skipped;
            AddEvent(request, userId, decision == ApprovalDecision.Approve ? RequestEventKinds.Approved : RequestEventKinds.Rejected,
                new { taskId = task.Id, stepId = task.StepId, stepName = task.StepName, comment = text }, now);
            request.UpdatedAt = now;

            if (result.Outcome == StepOutcome.Rejected)
            {
                request.Status = RequestStatus.Rejected;
                request.CompletedAt = now;
            }
            else if (result.Outcome == StepOutcome.Approved)
            {
                var values = FlowValues.FromJson(request.DataJson);
                var dir = await FlowDirectory.LoadAsync(db, request.WorkspaceId, def, ct);
                var requesterManager = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == request.WorkspaceId && m.UserId == request.RequesterId)
                    .Select(m => m.ManagerId).FirstOrDefaultAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await AdvanceAsync(request, app, def, values, new Requester(request.RequesterId, requesterManager), dir, task.StepIndex + 1, now, ct);
                if (!await TrySaveAsync(attempt, ct)) continue;
                await tx.CommitAsync(ct);
                return await GetAsync(userId, request.Id, ct);
            }

            if (!await TrySaveAsync(attempt, ct)) continue;
            return await GetAsync(userId, request.Id, ct);
        }
    }

    /// <summary>İyimser eşzamanlılık: aynı talepte eşzamanlı karar/iptal olursa baştan değerlendirilir (ikincisi 409 alır).</summary>
    private async Task<bool> TrySaveAsync(int attempt, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) when (attempt < MaxDecisionAttempts) { db.ChangeTracker.Clear(); return false; }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("concurrent_update", "Talep aynı anda değiştirildi. Tekrar deneyin."); }
    }

    // ============================================================== iç yardımcılar

    /// <summary>Akışı verilen adımdan ilerletir: atlananları işler, sıradaki adımın görevlerini açar veya talebi onaylanmış sayar.</summary>
    private async Task AdvanceAsync(FlowRequest request, AppDefinition app, FlowDefinition def, IReadOnlyDictionary<string, object?> values,
        Requester requester, FlowDirectory dir, int fromIndex, DateTimeOffset now, CancellationToken ct)
    {
        var adv = FlowEngine.Advance(def, fromIndex, values, requester, dir);
        foreach (var s in adv.Skipped) AddEvent(request, null, RequestEventKinds.StepSkipped, new { stepId = s.StepId, stepName = s.Name }, now);

        if (adv.Kind == AdvanceKind.AwaitApproval)
        {
            var plan = adv.Plan!;
            if (plan.Assignees.Count == 0)
                throw new ConflictException("no_approver", "Bu adım için onaylayıcı bulunamadı. Çalışma alanında bir master admin bulunmalı.");
            request.CurrentStepIndex = plan.Index;
            foreach (var a in plan.Assignees)
                db.ApprovalTasks.Add(new ApprovalTask
                {
                    Id = Guid.NewGuid(), RequestId = request.Id, WorkspaceId = request.WorkspaceId, StepIndex = plan.Index, StepId = plan.StepId,
                    StepName = plan.Name, Mode = plan.Mode, AssigneeId = a, Fallback = plan.FallbackReason, CreatedAt = now,
                });
            AddEvent(request, null, RequestEventKinds.StepStarted,
                new { stepId = plan.StepId, stepName = plan.Name, mode = plan.Mode.ToString().ToLowerInvariant(), assignees = plan.Assignees, fallback = plan.FallbackReason }, now);
            return;
        }

        request.Status = RequestStatus.Approved;
        request.CompletedAt = now;
        AddEvent(request, null, RequestEventKinds.Completed, new { status = "approved" }, now);
        await hooks.OnApprovedAsync(request, app, def, values, ct);
    }

    private void AddEvent(FlowRequest request, Guid? actorId, string kind, object? detail, DateTimeOffset at) =>
        db.RequestEvents.Add(new RequestEvent
        {
            RequestId = request.Id, WorkspaceId = request.WorkspaceId, At = at, ActorId = actorId, Kind = kind,
            DetailJson = detail is null ? null : ArcvoraJson.Serialize(detail),
        });

    /// <summary>Çalışma alanı + önek + yıl için artan sayaç; talep işlemiyle aynı işlemde artar, numaralar benzersizdir.</summary>
    private async Task<int> NextNumberAsync(Guid workspaceId, string prefix, int year, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO "RequestCounters" ("WorkspaceId", "Prefix", "Year", "Value") VALUES (@w, @p, @y, 1)
            ON CONFLICT ("WorkspaceId", "Prefix", "Year") DO UPDATE SET "Value" = "RequestCounters"."Value" + 1
            RETURNING "Value"
            """, conn, (NpgsqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction());
        cmd.Parameters.AddWithValue("w", workspaceId);
        cmd.Parameters.AddWithValue("p", prefix);
        cmd.Parameters.AddWithValue("y", year);
        return (int)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private async Task RequireUsableAsync(Guid userId, Membership member, AppDefinition app, CancellationToken ct)
    {
        if (!await access.CanUseAsync(userId, member, app.Id, ct))
            throw new AppException(403, "no_app_access", "Bu uygulamayı kullanma yetkiniz yok.");
        if (app.Status != AppStatus.Published || app.PublishedVersionId is null)
            throw new ConflictException("app_not_published", "Bu uygulama şu anda kullanıma açık değil.");
    }

    private Task<FlowRequest?> FindByKeyAsync(Guid userId, string key, CancellationToken ct) =>
        db.FlowRequests.AsNoTracking().FirstOrDefaultAsync(r => r.RequesterId == userId && r.IdempotencyKey == key, ct);

    private static IQueryable<FlowRequest> Filter(IQueryable<FlowRequest> q, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return q;
        var parsed = status.Trim().ToLowerInvariant() switch
        {
            "in_review" => (RequestStatus?)RequestStatus.InReview,
            "approved" => RequestStatus.Approved,
            "rejected" => RequestStatus.Rejected,
            "cancelled" => RequestStatus.Cancelled,
            _ => null,
        };
        if (parsed is null) throw new RequestValidationException("invalid_status", "Durum in_review, approved, rejected veya cancelled olmalı.");
        return q.Where(r => r.Status == parsed);
    }

    private async Task<IReadOnlyList<RequestSummaryDto>> ListAsync(IQueryable<FlowRequest> q, CancellationToken ct)
    {
        var requests = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(ListLimit).ToListAsync(ct);
        if (requests.Count == 0) return [];
        var appIds = requests.Select(r => r.AppId).Distinct().ToList();
        var userIds = requests.Select(r => r.RequesterId).Distinct().ToList();
        var reqIds = requests.Select(r => r.Id).ToList();
        var apps = await db.Apps.AsNoTracking().Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var steps = (await db.ApprovalTasks.AsNoTracking().Where(t => reqIds.Contains(t.RequestId) && t.Status == ApprovalStatus.Pending)
                .Select(t => new { t.RequestId, t.StepName }).ToListAsync(ct))
            .GroupBy(t => t.RequestId).ToDictionary(g => g.Key, g => g.First().StepName);
        return requests.Select(r => ToSummary(r, apps[r.AppId], names.GetValueOrDefault(r.RequesterId, "(eski kullanıcı)"),
            r.Status == RequestStatus.InReview ? steps.GetValueOrDefault(r.Id) : null)).ToList();
    }

    private static RequestSummaryDto ToSummary(FlowRequest r, AppDefinition app, string requesterName, string? currentStep) =>
        new(r.Id, r.WorkspaceId, r.Number, r.AppId, app.Name, app.Icon, Status(r.Status), r.Summary, r.RequesterId, requesterName,
            r.CreatedAt, r.CompletedAt, currentStep);

    private static MyAppDto ToMy(AppDefinition a, int version) => new(a.Id, a.WorkspaceId, a.Name, a.Description, a.Icon, version);

    public static string Status(RequestStatus s) => s switch
    {
        RequestStatus.InReview => "in_review",
        RequestStatus.Approved => "approved",
        RequestStatus.Rejected => "rejected",
        _ => "cancelled",
    };

    public static string Status(ApprovalStatus s) => s.ToString().ToLowerInvariant();
}
