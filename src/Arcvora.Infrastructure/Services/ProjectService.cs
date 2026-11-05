using Arcvora.Core.Ai;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Profiles;
using Arcvora.Core.Rendering;
using Arcvora.Core.Validation;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arcvora.Infrastructure.Services;

public sealed class ProjectService(ArcvoraDbContext db, ProjectAccessService access, VersionStore versions, AuditLog audit, TimeProvider clock)
{
    private const int MaxSourceLength = 100_000;
    private const int MaxPatchOperations = 500;

    // ------------------------------------------------------------ oluşturma / listeleme

    public async Task<ProjectDetail> CreateAsync(Guid userId, CreateProjectRequest req, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(userId, req.WorkspaceId, WorkspaceRole.Editor, ct);

        var name = (req.Name ?? "").Trim();
        if (name.Length is 0 or > 200) throw new RequestValidationException("invalid_name", "Proje adı 1–200 karakter olmalı.");
        var description = (req.Description ?? "").Trim();
        if (description.Length > 4000) throw new RequestValidationException("invalid_description", "Açıklama en fazla 4000 karakter olabilir.");

        var profile = ProfileCatalog.Find(string.IsNullOrWhiteSpace(req.TechProfileId) ? ProfileCatalog.AspNetCoreId : req.TechProfileId)
                      ?? throw new RequestValidationException("unknown_profile", "Bilinmeyen teknoloji profili.");
        if (!profile.IsSelectable)
            throw new RequestValidationException("profile_not_available",
                $"'{profile.Name}' profili henüz doğrulanmadı ve seçilemez.");

        var sources = new List<Source>();
        var idea = (req.IdeaText ?? "").Trim();
        if (idea.Length > MaxSourceLength) throw new RequestValidationException("source_too_long", "Fikir metni çok uzun.");
        if (idea.Length > 0) sources.Add(new Source { Id = "SRC-001", Title = "Proje fikri", Text = idea });

        var model = new ArchitectureModel
        {
            Project = new ProjectInfo { Name = name, Description = description, TechProfileId = profile.Id },
            Sources = sources,
        };

        var project = new Project
        {
            Id = Guid.NewGuid(), WorkspaceId = req.WorkspaceId, Name = name, Description = description, TechProfileId = profile.Id,
            HeadNumber = 0, CreatedBy = userId, CreatedAt = clock.GetUtcNow(),
        };
        db.Projects.Add(project);
        await versions.CommitAsync(project, null, model, userId, "Proje oluşturuldu", VersionKinds.Initial, null,
            v => audit.Add(project.WorkspaceId, project.Id, userId, "project.created", "project", project.Id.ToString()), ct);

        return await GetAsync(userId, project.Id, ct);
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = await (from p in db.Projects.AsNoTracking()
                          join m in db.Memberships.AsNoTracking() on p.WorkspaceId equals m.WorkspaceId
                          where m.UserId == userId && m.Role != WorkspaceRole.AppUser
                          select new { p, m.Role }).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ids = rows.Select(r => r.p.Id).ToList();
        var updated = await db.ModelVersions.AsNoTracking().Where(v => ids.Contains(v.ProjectId))
            .GroupBy(v => v.ProjectId).Select(g => new { g.Key, At = g.Max(v => v.CreatedAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);
        var pending = await db.Proposals.AsNoTracking().Where(p => ids.Contains(p.ProjectId) && p.Status == ProposalStatus.Pending)
            .GroupBy(p => p.ProjectId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        return rows.OrderByDescending(r => updated.GetValueOrDefault(r.p.Id)).Select(r => Summary(r.p, r.Role,
            updated.GetValueOrDefault(r.p.Id, r.p.CreatedAt), pending.GetValueOrDefault(r.p.Id))).ToList();
    }

    public async Task<ProjectDetail> GetAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var head = await versions.GetHeadAsync(ctx.Project, ct);
        var model = VersionStore.Load(head);
        var pending = await db.Proposals.CountAsync(p => p.ProjectId == projectId && p.Status == ProposalStatus.Pending, ct);

        return new ProjectDetail(Summary(ctx.Project, ctx.Membership.Role, head.CreatedAt, pending), VersionStore.ToInfo(head), model,
            ModelValidator.Validate(model).Findings, ctx.Can(Permission.Edit), ctx.Can(Permission.Accept), ctx.Can(Permission.Admin));
    }

    public async Task DeleteAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Admin, track: false, ct);
        audit.Add(ctx.Project.WorkspaceId, projectId, userId, "project.deleted", "project", projectId.ToString(), new { ctx.Project.Name });
        await db.SaveChangesAsync(ct);
        await db.Projects.Where(p => p.Id == projectId).ExecuteDeleteAsync(ct);   // ilişkili tüm kayıtlar FK ile silinir
    }

    // ------------------------------------------------------------ sürümler / görünümler

    public async Task<IReadOnlyList<VersionInfo>> ListVersionsAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var list = await db.ModelVersions.AsNoTracking().Where(v => v.ProjectId == projectId).OrderByDescending(v => v.Number)
            .Select(v => new VersionInfo(v.Id, v.Number, v.Summary, v.Kind, v.ContentHash, v.CreatedBy, v.CreatedAt, v.ParentVersionId, v.ProposalId))
            .ToListAsync(ct);
        return list;
    }

    public async Task<VersionDetail> GetVersionAsync(Guid userId, Guid projectId, int number, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var v = await versions.GetAsync(projectId, number, ct);
        return new VersionDetail(VersionStore.ToInfo(v), VersionStore.Load(v));
    }

    public async Task<ViewsResult> GetViewsAsync(Guid userId, Guid projectId, int number, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var v = await versions.GetAsync(projectId, number, ct);
        return new ViewsResult(v.Number, v.ContentHash, ViewRenderer.All(VersionStore.Load(v)));
    }

    public async Task<FindingsResult> GetFindingsAsync(Guid userId, Guid projectId, int number, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        var v = await versions.GetAsync(projectId, number, ct);
        return new FindingsResult(v.Number, ModelValidator.Validate(VersionStore.Load(v)).Findings);
    }

    /// <summary>`versions/{x}` yolu sürüm numarası veya sürüm kimliği (GUID) kabul eder.</summary>
    public async Task<int> ResolveVersionNumberAsync(Guid userId, Guid projectId, string key, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Read, track: false, ct);
        if (int.TryParse(key, out var n) && n > 0) return n;
        if (Guid.TryParse(key, out var id))
            return (await versions.GetByIdAsync(projectId, id, ct)).Number;
        throw new BadRequestException("Sürüm numarası veya kimliği geçersiz.");
    }

    public async Task<IReadOnlyList<AuditItem>> AuditAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        await access.RequireAsync(userId, projectId, Permission.Admin, track: false, ct);
        return await (from a in db.AuditEntries.AsNoTracking()
                      join u in db.Users.AsNoTracking() on a.ActorId equals u.Id into us
                      from u in us.DefaultIfEmpty()
                      where a.ProjectId == projectId
                      orderby a.Id descending
                      select new AuditItem(a.Id, a.At, a.Action, u == null ? null : u.Email, a.ObjectType, a.ObjectId, a.DetailJson))
            .Take(200).ToListAsync(ct);
    }

    // ------------------------------------------------------------ düzenleme

    public async Task<EditResult> AddSourceAsync(Guid userId, Guid projectId, AddSourceRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Edit, track: true, ct);
        var text = (req.Text ?? "").Trim();
        if (text.Length == 0) throw new RequestValidationException("empty_source", "Kaynak metni boş olamaz.");
        if (text.Length > MaxSourceLength) throw new RequestValidationException("source_too_long", $"Kaynak en fazla {MaxSourceLength} karakter olabilir.");
        var format = req.Format is "markdown" ? "markdown" : "text";

        return await EditAsync(ctx, req.BaseVersion, "Kaynak eklendi", (model) =>
        {
            var id = Ids.Next(Ids.Prefix(ObjectType.Source), model.Sources.Select(s => s.Id));
            var src = new Source { Id = id, Title = (req.Title ?? "").Trim(), Text = text, Format = format };
            return (new ModelPatch { Operations = [ModelPatch.Upsert(ObjectType.Source, src)] }, $"Kaynak eklendi: {id}");
        }, ct);
    }

    public async Task<EditResult> UpdateRequirementAsync(Guid userId, Guid projectId, string requirementId, UpdateRequirementRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Edit, track: true, ct);
        return await EditAsync(ctx, req.BaseVersion, "Gereksinim düzenlendi", model =>
        {
            var current = model.Requirements.FirstOrDefault(r => r.Id == requirementId) ?? throw new NotFoundException("Gereksinim bulunamadı.");
            var updated = current;

            if (req.Text is not null)
            {
                var t = req.Text.Trim();
                if (t.Length is 0 or > 2000) throw new RequestValidationException("invalid_text", "Gereksinim metni 1–2000 karakter olmalı.");
                updated = updated with { Text = t };
            }
            if (req.Kind is { } k) updated = updated with { Kind = k };
            if (req.Priority is { } p) updated = updated with { Priority = p };
            if (req.ReviewStatus is { } rs) updated = updated with { ReviewStatus = rs };

            if (req.AcceptanceCriteria is not null)
            {
                var used = model.Requirements.SelectMany(r => r.AcceptanceCriteria).Select(a => a.Id).ToHashSet();
                var list = new List<AcceptanceCriterion>();
                foreach (var c in req.AcceptanceCriteria)
                {
                    var text = (c.Text ?? "").Trim();
                    if (text.Length is 0 or > 1000) throw new RequestValidationException("invalid_criterion", "Kabul ölçütü 1–1000 karakter olmalı.");
                    var own = current.AcceptanceCriteria.Any(a => a.Id == c.Id);
                    var id = c.Id is not null && own ? c.Id : Ids.Next(Ids.AcceptancePrefix, used);
                    used.Add(id);
                    list.Add(new AcceptanceCriterion { Id = id, Text = text });
                }
                updated = updated with { AcceptanceCriteria = list };
            }

            return (new ModelPatch { Operations = [ModelPatch.Upsert(ObjectType.Requirement, updated)] }, $"Gereksinim düzenlendi: {requirementId}");
        }, ct);
    }

    public async Task<EditResult> AnswerQuestionAsync(Guid userId, Guid projectId, string questionId, AnswerQuestionRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Edit, track: true, ct);
        return await EditAsync(ctx, req.BaseVersion, "Soru güncellendi", model =>
        {
            var q = model.OpenQuestions.FirstOrDefault(x => x.Id == questionId) ?? throw new NotFoundException("Soru bulunamadı.");
            var answer = req.Answer?.Trim();
            if (answer is { Length: > 2000 }) throw new RequestValidationException("invalid_answer", "Cevap en fazla 2000 karakter olabilir.");
            var status = req.Status ?? (string.IsNullOrEmpty(answer) ? q.Status : QuestionStatus.Answered);
            if (status == QuestionStatus.Answered && string.IsNullOrWhiteSpace(answer ?? q.Answer))
                throw new RequestValidationException("answer_required", "Cevaplandı olarak işaretlemek için cevap metni gerekli.");
            var updated = q with { Status = status, Answer = answer ?? q.Answer, ReviewStatus = ReviewStatus.Confirmed };
            return (new ModelPatch { Operations = [ModelPatch.Upsert(ObjectType.OpenQuestion, updated)] },
                status == QuestionStatus.Answered ? $"Soru cevaplandı: {questionId}" : $"Soru ertelendi/yeniden açıldı: {questionId}");
        }, ct);
    }

    /// <summary>Yapılandırılmış düzenleyici: kullanıcı kaynaklı patch (§14.3). Kullanıcı gözden geçiren olduğundan uygulanan nesneler onaylanır.</summary>
    public async Task<EditResult> ApplyPatchAsync(Guid userId, Guid projectId, ApplyPatchRequest req, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Edit, track: true, ct);
        if (req.Patch is null || req.Patch.IsEmpty) throw new RequestValidationException("empty_patch", "Patch boş.");
        if (req.Patch.Operations.Count > MaxPatchOperations)
            throw new RequestValidationException("patch_too_large", $"Bir patch en fazla {MaxPatchOperations} işlem içerebilir.");
        if (req.Patch.Operations.Any(o => o.Type == ObjectType.Source && o.Op == PatchOpKind.Remove))
            throw new RequestValidationException("source_removal", "Kaynaklar silinemez; izlenebilirlik kaybolur.");

        return await EditAsync(ctx, req.BaseVersion, "Model düzenlendi", model =>
        {
            var resolved = DraftResolver.Resolve(model, req.Patch);
            return (resolved, string.IsNullOrWhiteSpace(req.Summary) ? "Model düzenlendi" : req.Summary.Trim());
        }, ct);
    }

    public async Task<EditResult> RevertAsync(Guid userId, Guid projectId, int number, int? baseVersion, CancellationToken ct)
    {
        var ctx = await access.RequireAsync(userId, projectId, Permission.Accept, track: true, ct);
        VersionStore.EnsureBase(ctx.Project, baseVersion);
        if (number == ctx.Project.HeadNumber)
            throw new RequestValidationException("already_head", "Bu sürüm zaten güncel.");

        var head = await versions.GetHeadAsync(ctx.Project, ct);
        var target = await versions.GetAsync(projectId, number, ct);
        var targetModel = VersionStore.Load(target);
        var created = await versions.CommitAsync(ctx.Project, VersionStore.Load(head), targetModel, userId,
            $"v{number} sürümüne geri dönüldü", VersionKinds.Revert, null,
            v => audit.Add(ctx.Project.WorkspaceId, projectId, userId, "version.reverted", "version", v.Number.ToString(), new { to = number }), ct);
        return new EditResult(VersionStore.ToInfo(created), []);
    }

    private async Task<EditResult> EditAsync(ProjectAccessContext ctx, int? baseVersion, string defaultSummary,
        Func<ArchitectureModel, (ModelPatch Patch, string Summary)> build, CancellationToken ct)
    {
        VersionStore.EnsureBase(ctx.Project, baseVersion);
        var head = await versions.GetHeadAsync(ctx.Project, ct);
        var model = VersionStore.Load(head);

        var (patch, summary) = build(model);
        var applied = ModelPatcher.Apply(model, patch, confirmApplied: true);
        if (!applied.Ok) throw new RequestValidationException("patch_invalid", "Patch uygulanamadı.", applied.Errors);

        var before = ModelValidator.Validate(model).Findings.Select(f => f.Key).ToHashSet();
        var created = await versions.CommitAsync(ctx.Project, model, applied.Model!, ctx.UserId, string.IsNullOrWhiteSpace(summary) ? defaultSummary : summary,
            VersionKinds.Edit, null,
            v => audit.Add(ctx.Project.WorkspaceId, ctx.Project.Id, ctx.UserId, "model.edited", "version", v.Number.ToString(),
                new { summary, ops = patch.Operations.Select(o => new { o.Op, o.Type, o.Id }).ToList() }), ct);

        var newFindings = ModelValidator.Validate(applied.Model!).Findings.Where(f => !before.Contains(f.Key)).ToList();
        return new EditResult(VersionStore.ToInfo(created), newFindings);
    }

    private static ProjectSummary Summary(Project p, WorkspaceRole role, DateTimeOffset updated, int pending) =>
        new(p.Id, p.WorkspaceId, p.Name, p.Description, p.TechProfileId, p.HeadNumber, updated, pending, role.ToString().ToLowerInvariant());
}
