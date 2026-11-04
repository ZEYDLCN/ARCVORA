using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Samples;
using Arcvora.Core.Validation;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;

namespace Arcvora.Infrastructure.Services;

/// <summary>
/// Örnek projeyi oluşturur (§9): elle hazırlanmış masraf yönetimi modeli v1 olarak kaydedilir ve §9.5'teki iki aşamalı
/// onay değişikliği bekleyen bir öneri olarak eklenir. Bu bir AI çıktısı değildir; öneri bunu açıkça belirtir.
/// </summary>
public sealed class SampleService(ArcvoraDbContext db, ProjectAccessService access, VersionStore versions, ProposalService proposals,
    AuditLog audit, TimeProvider clock)
{
    public async Task<ProjectDetail> CreateExpenseSampleAsync(Guid userId, Guid workspaceId, ProjectService projectsSvc, CancellationToken ct)
    {
        await access.RequireWorkspaceRoleAsync(userId, workspaceId, WorkspaceRole.Editor, ct);

        var model = ExpenseSample.Model();
        var project = new Project
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Name = ExpenseSample.ProjectName, Description = model.Project.Description,
            TechProfileId = model.Project.TechProfileId, HeadNumber = 0, CreatedBy = userId, CreatedAt = clock.GetUtcNow(),
        };
        db.Projects.Add(project);

        var (patch, possible) = ExpenseSample.TwoStageApprovalChange();
        var after = ModelPatcher.Apply(model, patch).Model!;
        var impact = ImpactAnalyzer.Analyze(model, after, patch, possible);
        var before = ModelValidator.Validate(model).Findings.Select(f => f.Key).ToHashSet();
        var newFindings = ModelValidator.Validate(after).Findings.Where(f => !before.Contains(f.Key)).ToList();

        var v1 = await versions.CommitAsync(project, null, model, userId, "Örnek proje: masraf yönetimi (elle hazırlanmış model)",
            VersionKinds.Initial, null, v =>
            {
                audit.Add(workspaceId, project.Id, userId, "project.created", "project", project.Id.ToString(), new { sample = "expense" });
                proposals.Stage(project.Id, userId, new PendingProposal(
                    ProposalKinds.Sample, "İki aşamalı şirket onay politikası", ExpenseSample.ChangeRequestText, v.Id, v.Number, patch, impact,
                    newFindings, "sample", false,
                    "Elle hazırlanmış örnek değişiklik; yapay zekâ çıktısı değildir. Devam eden masrafların politika sürümü sorusu (OQ-004) cevaplanmadığı için ilgili karar geçici varsayımla 'ertelendi' işaretlidir.",
                    0, 0), null);
            }, ct);

        return await projectsSvc.GetAsync(userId, project.Id, ct);
    }
}
