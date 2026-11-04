using System.Text.Json;
using Arcvora.Core.Ai;
using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Profiles;
using Arcvora.Core.Validation;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Jobs;

/// <summary>Yeniden denemenin işe yaramayacağı iş hatası.</summary>
public sealed class JobFailureException(string code, string message, IReadOnlyList<string>? problems = null, string? stage = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public string? Stage { get; } = stage;
    public IReadOnlyList<string> Problems { get; } = problems ?? [];
}

/// <summary>
/// Gereksinim çıkarma, mimari taslak ve değişiklik analizi işlerini çalıştırır. Projeye YAZMAZ: yalnızca bir
/// öneri (taslak) üretir; kabul kararı ve yazım <see cref="ProposalService"/> kurallarındadır.
/// </summary>
public sealed class GenerationJobHandler(ArcvoraDbContext db, VersionStore versions, IArchitectureGenerator generator, IOptions<AiOptions> ai)
{
    public async Task<PendingProposal> RunAsync(Job job, Func<string, CancellationToken, Task> onStage, CancellationToken ct)
    {
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == job.ProjectId, ct)
                      ?? throw new JobFailureException("project_missing", "Proje bulunamadı.");
        var head = await versions.GetHeadAsync(project, ct);
        var model = VersionStore.Load(head);
        var profile = ProfileCatalog.Find(model.Project.TechProfileId) ?? ProfileCatalog.AspNetCore;
        var budget = new PipelineBudget(job.TokenBudget > 0 ? job.TokenBudget : ai.Value.MaxTokensPerJob, ai.Value.MaxRepairs,
            TimeSpan.FromSeconds(ai.Value.TimeoutSeconds));
        var pipeline = new ProposalPipeline(generator);

        try
        {
            return job.Kind switch
            {
                JobKinds.Analysis => await Extract(pipeline, model, profile, budget, head, onStage, ct),
                JobKinds.Architecture => await Architecture(pipeline, model, profile, budget, head, onStage, ct),
                JobKinds.Change => await Change(pipeline, job, model, profile, budget, head, onStage, ct),
                _ => throw new JobFailureException("unknown_kind", $"Bilinmeyen iş türü '{job.Kind}'."),
            };
        }
        catch (PipelineException ex)
        {
            throw new JobFailureException(ex.Stage, ex.Message, ex.Problems, ex.Stage);
        }
    }

    private async Task<PendingProposal> Extract(ProposalPipeline pipeline, ArchitectureModel model, TechProfile profile,
        PipelineBudget budget, ModelVersion head, Func<string, CancellationToken, Task> onStage, CancellationToken ct)
    {
        var analyzed = model.Requirements.SelectMany(r => r.SourceRefs).Select(s => s.SourceId).ToHashSet();
        if (model.Sources.All(s => analyzed.Contains(s.Id)))
            throw new JobFailureException("no_pending_sources", "Analiz edilecek yeni kaynak yok.");

        var r = await pipeline.RunAsync(GenerationTask.Extract, new GenerationContext(model, profile), budget, onStage, ct);
        return Pending(ProposalKinds.Analysis, "Gereksinim çıkarımı", null, head, model, r.Patch, r.Impact, r, r.Result);
    }

    private async Task<PendingProposal> Architecture(ProposalPipeline pipeline, ArchitectureModel model, TechProfile profile,
        PipelineBudget budget, ModelVersion head, Func<string, CancellationToken, Task> onStage, CancellationToken ct)
    {
        if (!model.Requirements.Any(r => r.ReviewStatus != ReviewStatus.Rejected))
            throw new JobFailureException("no_requirements", "Mimari taslak için gereksinim yok.");

        var r = await pipeline.RunAsync(GenerationTask.Architecture, new GenerationContext(model, profile), budget, onStage, ct);
        return Pending(ProposalKinds.Architecture, "Mimari taslak", null, head, model, r.Patch, r.Impact, r, r.Result);
    }

    private async Task<PendingProposal> Change(ProposalPipeline pipeline, Job job, ArchitectureModel model, TechProfile profile,
        PipelineBudget budget, ModelVersion head, Func<string, CancellationToken, Task> onStage, CancellationToken ct)
    {
        var text = JsonDocument.Parse(job.InputJson).RootElement.GetProperty("requestText").GetString() ?? "";

        // Talep metni de bir kaynak olur (§14.1 Source): üretilen gereksinimler ona atıf yapabilir.
        var sourceId = Ids.Next(Ids.Prefix(ObjectType.Source), model.Sources.Select(s => s.Id));
        var sourceOp = ModelPatch.Upsert(ObjectType.Source, new Source { Id = sourceId, Title = "Değişiklik talebi", Text = text }, "grp-request-source");
        var prefix = new ModelPatch
        {
            Operations = [sourceOp],
            Groups = [new PatchGroup { Id = "grp-request-source", Title = "Talep kaynak olarak kaydedilir" }],
        };
        var working = ModelPatcher.Apply(model, prefix).Model!;

        var r = await pipeline.RunAsync(GenerationTask.Change, new GenerationContext(working, profile, text), budget, onStage, ct);

        var full = new ModelPatch
        {
            Summary = r.Patch.Summary,
            Operations = [sourceOp, .. r.Patch.Operations],
            Groups = [.. prefix.Groups, .. r.Patch.Groups],
        };
        var after = ModelPatcher.Apply(model, full).Model!;
        var impact = ImpactAnalyzer.Analyze(model, after, full, r.PossibleImpacts);
        var title = text.Length <= 80 ? text : text[..77] + "…";
        return Pending(ProposalKinds.Change, title, text, head, model, full, impact, r, after);
    }

    private static PendingProposal Pending(string kind, string title, string? request, ModelVersion head, ArchitectureModel baseModel,
        ModelPatch patch, ImpactReport impact, PipelineResult r, ArchitectureModel result)
    {
        var before = ModelValidator.Validate(baseModel).Findings.Select(f => f.Key).ToHashSet();
        var fresh = ModelValidator.Validate(result).Findings.Where(f => !before.Contains(f.Key)).ToList();
        return new PendingProposal(kind, title, request, head.Id, head.Number, patch, impact, fresh,
            r.Provider, r.IsAi, JoinNotes(r), r.TokensUsed, r.RepairsUsed);
    }

    private static string? JoinNotes(PipelineResult r)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(r.Note)) parts.Add(r.Note!);
        parts.AddRange(r.GuardNotes);
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
