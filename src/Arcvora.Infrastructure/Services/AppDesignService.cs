using Arcvora.Core.Flows;
using Arcvora.Core.Model;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Arcvora.Infrastructure.Services;

/// <summary>Akış uygulamalarının tasarımı, yayını ve kullanım yetkileri. Yalnızca master admin.</summary>
public sealed class AppDesignService(ArcvoraDbContext db, AppAccessService access, AuditLog audit, TimeProvider clock)
{
    public static readonly IReadOnlySet<string> Icons = new HashSet<string>
    {
        "file", "calendar", "wallet", "receipt", "plane", "briefcase", "users", "clock", "check", "shield", "car", "home",
    };

    private const int MaxDefinitionBytes = 200_000;

    // ------------------------------------------------------------------ liste / ayrıntı

    public async Task<IReadOnlyList<AppSummaryDto>> ListAsync(Guid actorId, Guid workspaceId, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var apps = await db.Apps.AsNoTracking().Where(a => a.WorkspaceId == workspaceId).OrderBy(a => a.Name).ToListAsync(ct);
        var hashes = await LatestHashesAsync(apps.Select(a => a.Id).ToList(), ct);
        return apps.Select(a => Summary(a, hashes.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<AppDetailDto> GetAsync(Guid actorId, Guid appId, CancellationToken ct)
    {
        var (app, member) = await access.LoadAppAsync(actorId, appId, false, ct);
        if (member.Role != WorkspaceRole.Admin) throw new ForbiddenException("Bu işlem için master admin yetkisi gerekir.");
        return await DetailAsync(app, ct);
    }

    // ---------------------------------------------------------------------- oluştur / düzenle

    public async Task<AppDetailDto> CreateAsync(Guid actorId, Guid workspaceId, CreateAppRequest req, CancellationToken ct)
    {
        await access.RequireAdminAsync(actorId, workspaceId, ct);
        var (name, description, icon) = Clean(req.Name, req.Description, req.Icon, requireName: true);
        var def = req.Definition ?? new FlowDefinition { Prefix = "APP" };
        var now = clock.GetUtcNow();
        var app = new AppDefinition
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = name!, Description = description ?? "", Icon = icon ?? "file",
            Status = AppStatus.Draft, DraftJson = Serialize(def), Revision = 1, CreatedBy = actorId, CreatedAt = now, UpdatedAt = now,
        };
        db.Apps.Add(app);
        audit.Add(workspaceId, null, actorId, "app.created", "app", app.Id.ToString(), new { app.Name });
        await db.SaveChangesAsync(ct);
        return await DetailAsync(app, ct);
    }

    public async Task<AppDetailDto> UpdateAsync(Guid actorId, Guid appId, UpdateAppRequest req, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct);
        if (req.BaseRevision is { } rev && rev != app.Revision)
            throw new ConflictException("revision_conflict", "Taslak başka biri tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin.",
                new { currentRevision = app.Revision });

        var (name, description, icon) = Clean(req.Name, req.Description, req.Icon, requireName: false);
        if (name is not null) app.Name = name;
        if (description is not null) app.Description = description;
        if (icon is not null) app.Icon = icon;
        if (req.Definition is not null) app.DraftJson = Serialize(req.Definition);
        app.Revision++;
        app.UpdatedAt = clock.GetUtcNow();
        audit.Add(app.WorkspaceId, null, actorId, "app.updated", "app", app.Id.ToString(), new { app.Revision });
        await db.SaveChangesAsync(ct);
        return await DetailAsync(app, ct);
    }

    // ------------------------------------------------------------------------- yayın

    public async Task<AppDetailDto> PublishAsync(Guid actorId, Guid appId, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct);
        var def = Deserialize(app.DraftJson);
        var issues = await ValidateAsync(app.WorkspaceId, def, ct);
        if (issues.Count > 0)
            throw new RequestValidationException("invalid_definition", "Akış tanımı yayınlanamaz: düzeltilmesi gereken sorunlar var.", issues);

        var hash = FlowValues.Hash(def);
        var latestHash = (await LatestHashesAsync([app.Id], ct)).GetValueOrDefault(app.Id);
        if (latestHash == hash && app.Status == AppStatus.Published)
            throw new ConflictException("nothing_to_publish", "Yayınlanmış sürümden farklı bir değişiklik yok.");

        var now = clock.GetUtcNow();
        var version = new AppVersion
        {
            Id = Guid.NewGuid(), AppId = app.Id, Number = app.VersionCount + 1, DefinitionJson = Serialize(def), Hash = hash,
            PublishedBy = actorId, PublishedAt = now,
        };
        db.AppVersions.Add(version);
        app.VersionCount = version.Number;
        app.PublishedVersionId = version.Id;
        app.Status = AppStatus.Published;
        app.UpdatedAt = now;
        audit.Add(app.WorkspaceId, null, actorId, "app.published", "app", app.Id.ToString(), new { version = version.Number });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("publish_conflict", "Aynı anda başka bir yayın yapıldı. Sayfayı yenileyip tekrar deneyin.");
        }
        return await DetailAsync(app, ct);
    }

    public async Task<AppSummaryDto> SetArchivedAsync(Guid actorId, Guid appId, bool archived, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct);
        app.Status = archived ? AppStatus.Archived : app.PublishedVersionId is null ? AppStatus.Draft : AppStatus.Published;
        app.UpdatedAt = clock.GetUtcNow();
        audit.Add(app.WorkspaceId, null, actorId, archived ? "app.archived" : "app.restored", "app", app.Id.ToString());
        await db.SaveChangesAsync(ct);
        return Summary(app, (await LatestHashesAsync([app.Id], ct)).GetValueOrDefault(app.Id));
    }

    /// <summary>Hiç yayınlanmamış taslaklar silinir; yayınlanmışlar (talep geçmişi olabilir) yalnızca arşivlenir.</summary>
    public async Task DeleteAsync(Guid actorId, Guid appId, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct);
        if (app.VersionCount > 0) throw new ConflictException("app_published", "Yayınlanmış bir uygulama silinemez; arşivleyebilirsiniz.");
        db.Apps.Remove(app);
        audit.Add(app.WorkspaceId, null, actorId, "app.deleted", "app", app.Id.ToString(), new { app.Name });
        await db.SaveChangesAsync(ct);
    }

    // --------------------------------------------------------------------- yetkiler

    public async Task<AppGrantsDto> GetGrantsAsync(Guid actorId, Guid appId, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct, track: false);
        return await GrantsAsync(app.Id, ct);
    }

    public async Task<AppGrantsDto> SetGrantsAsync(Guid actorId, Guid appId, SetGrantsRequest req, CancellationToken ct)
    {
        var app = await RequireDesignerAsync(actorId, appId, ct, track: false);
        var users = (req.UserIds ?? []).Distinct().ToList();
        var groups = (req.GroupIds ?? []).Distinct().ToList();
        var validUsers = await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == app.WorkspaceId && users.Contains(m.UserId)).CountAsync(ct);
        var validGroups = await db.Groups.AsNoTracking().Where(g => g.WorkspaceId == app.WorkspaceId && groups.Contains(g.Id)).CountAsync(ct);
        if (validUsers != users.Count || validGroups != groups.Count)
            throw new RequestValidationException("invalid_grant", "Yetki yalnızca bu çalışma alanının üyelerine ve gruplarına verilebilir.");

        db.AppGrants.RemoveRange(await db.AppGrants.Where(g => g.AppId == appId).ToListAsync(ct));
        db.AppGrants.AddRange(users.Select(u => new AppGrant { AppId = appId, Kind = GrantKinds.User, SubjectId = u }));
        db.AppGrants.AddRange(groups.Select(g => new AppGrant { AppId = appId, Kind = GrantKinds.Group, SubjectId = g }));
        audit.Add(app.WorkspaceId, null, actorId, "app.grants_set", "app", appId.ToString(), new { users = users.Count, groups = groups.Count });
        await db.SaveChangesAsync(ct);
        return await GrantsAsync(appId, ct);
    }

    // ---------------------------------------------------------------- doğrulama

    /// <summary>Çekirdek kurallar + çalışma alanına bağlı kontroller (onaylayıcı kişi/grup gerçekten var mı).</summary>
    public async Task<IReadOnlyList<FlowIssue>> ValidateAsync(Guid workspaceId, FlowDefinition def, CancellationToken ct)
    {
        var issues = FlowValidator.Validate(def).ToList();

        var groupIds = def.Steps.Where(s => s.Approvers is { Type: ApproverType.Group, Id: not null }).Select(s => s.Approvers.Id!.Value).Distinct().ToList();
        var userIds = def.Steps.Where(s => s.Approvers is { Type: ApproverType.User, Id: not null }).Select(s => s.Approvers.Id!.Value).Distinct().ToList();
        var knownGroups = groupIds.Count == 0 ? [] : await db.Groups.AsNoTracking().Where(g => g.WorkspaceId == workspaceId && groupIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        var knownUsers = userIds.Count == 0 ? [] : await db.Memberships.AsNoTracking().Where(m => m.WorkspaceId == workspaceId && userIds.Contains(m.UserId)).Select(m => m.UserId).ToListAsync(ct);

        for (var i = 0; i < def.Steps.Count; i++)
        {
            var a = def.Steps[i].Approvers;
            if (a is { Type: ApproverType.Group, Id: { } g } && !knownGroups.Contains(g))
                issues.Add(new FlowIssue("approver_not_found", $"steps[{i}].approvers.id", "Seçilen grup bu çalışma alanında yok."));
            if (a is { Type: ApproverType.User, Id: { } u } && !knownUsers.Contains(u))
                issues.Add(new FlowIssue("approver_not_found", $"steps[{i}].approvers.id", "Seçilen kişi bu çalışma alanında yok."));
        }
        return issues;
    }

    // ------------------------------------------------------------------- yardımcılar

    private async Task<AppDefinition> RequireDesignerAsync(Guid actorId, Guid appId, CancellationToken ct, bool track = true)
    {
        var (app, member) = await access.LoadAppAsync(actorId, appId, track, ct);
        if (member.Role != WorkspaceRole.Admin) throw new ForbiddenException("Bu işlem için master admin yetkisi gerekir.");
        return app;
    }

    private async Task<AppDetailDto> DetailAsync(AppDefinition app, CancellationToken ct)
    {
        var draft = Deserialize(app.DraftJson);
        FlowDefinition? published = null;
        string? publishedHash = null;
        if (app.PublishedVersionId is { } vid)
        {
            var v = await db.AppVersions.AsNoTracking().FirstAsync(x => x.Id == vid, ct);
            published = Deserialize(v.DefinitionJson);
            publishedHash = v.Hash;
        }
        return new AppDetailDto(Summary(app, publishedHash), draft, published, await ValidateAsync(app.WorkspaceId, draft, ct));
    }

    private async Task<Dictionary<Guid, string>> LatestHashesAsync(IReadOnlyCollection<Guid> appIds, CancellationToken ct) =>
        (await db.Apps.AsNoTracking().Where(a => appIds.Contains(a.Id) && a.PublishedVersionId != null)
            .Join(db.AppVersions.AsNoTracking(), a => a.PublishedVersionId, v => v.Id, (a, v) => new { a.Id, v.Hash }).ToListAsync(ct))
        .ToDictionary(x => x.Id, x => x.Hash);

    private static AppSummaryDto Summary(AppDefinition a, string? publishedHash)
    {
        var draftHash = FlowValues.Hash(Deserialize(a.DraftJson));
        return new AppSummaryDto(a.Id, a.WorkspaceId, a.Name, a.Description, a.Icon, a.Status.ToString().ToLowerInvariant(),
            a.VersionCount, publishedHash is null || publishedHash != draftHash, a.Revision, a.UpdatedAt);
    }

    private async Task<AppGrantsDto> GrantsAsync(Guid appId, CancellationToken ct)
    {
        var grants = await db.AppGrants.AsNoTracking().Where(g => g.AppId == appId).ToListAsync(ct);
        return new AppGrantsDto(
            grants.Where(g => g.Kind == GrantKinds.User).Select(g => g.SubjectId).OrderBy(x => x).ToList(),
            grants.Where(g => g.Kind == GrantKinds.Group).Select(g => g.SubjectId).OrderBy(x => x).ToList());
    }

    private static (string? Name, string? Description, string? Icon) Clean(string? name, string? description, string? icon, bool requireName)
    {
        string? n = null;
        if (name is not null || requireName)
        {
            n = (name ?? "").Trim();
            if (n.Length is 0 or > 200) throw new RequestValidationException("invalid_name", "Ad 1–200 karakter olmalı.");
        }
        var d = description?.Trim();
        if (d is { Length: > 1000 }) throw new RequestValidationException("invalid_description", "Açıklama en fazla 1000 karakter olabilir.");
        if (icon is not null && !Icons.Contains(icon)) throw new RequestValidationException("invalid_icon", "Geçersiz simge.");
        return (n, d, icon);
    }

    private static string Serialize(FlowDefinition def)
    {
        var json = ArcvoraJson.Serialize(def);
        if (json.Length > MaxDefinitionBytes) throw new RequestValidationException("definition_too_large", "Akış tanımı çok büyük.");
        return json;
    }

    internal static FlowDefinition Deserialize(string json) => ArcvoraJson.Deserialize<FlowDefinition>(string.IsNullOrWhiteSpace(json) ? "{}" : json);
}
