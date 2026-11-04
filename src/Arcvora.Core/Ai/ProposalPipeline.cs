using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Ai;

/// <summary>İş başına token, süre ve onarım bütçesi (§15.3). Sınırsız düzeltme döngüsü yoktur.</summary>
public sealed record PipelineBudget(int MaxTokens = 120_000, int MaxRepairs = 2, TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromMinutes(5);
}

public sealed record PipelineResult(
    ModelPatch Patch,
    ArchitectureModel Result,
    ValidationReport Validation,
    ImpactReport Impact,
    IReadOnlyList<PossibleImpact> PossibleImpacts,
    string Provider,
    bool IsAi,
    int TokensUsed,
    int RepairsUsed,
    IReadOnlyList<string> GuardNotes,
    string? Note);

public sealed class PipelineException(string stage, string message, IReadOnlyList<string>? problems = null) : Exception(message)
{
    /// <summary>generate | resolve | apply | validate | budget | timeout</summary>
    public string Stage { get; } = stage;
    public IReadOnlyList<string> Problems { get; } = problems ?? [];
}

/// <summary>
/// Üretim → kimlik çözümü → koruyucu → patch uygulama → doğrulama → (sınırlı onarım) → etki analizi.
/// Doğrulayıcıdan geçmeyen taslak kabul edilebilir öneri olarak sunulmaz (§13.2).
/// </summary>
public sealed class ProposalPipeline(IArchitectureGenerator generator)
{
    public async Task<PipelineResult> RunAsync(GenerationTask task, GenerationContext ctx, PipelineBudget budget,
        Func<string, CancellationToken, Task>? onStage = null, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(budget.EffectiveTimeout);
        var token = timeout.Token;

        var tokens = 0;
        var repairs = 0;
        var notes = new List<string>();

        async Task Stage(string name)
        {
            if (onStage is not null) await onStage(name, token);
        }

        try
        {
            await Stage("generate");
            var output = task switch
            {
                GenerationTask.Extract => await generator.ExtractRequirementsAsync(ctx, token),
                GenerationTask.Architecture => await generator.ProposeArchitectureAsync(ctx, token),
                _ => await generator.AnalyzeChangeAsync(ctx, token),
            };
            tokens += output.InputTokens + output.OutputTokens;

            while (true)
            {
                CheckBudget(tokens, budget);

                await Stage("validate");
                var attempt = Evaluate(task, ctx, output);
                if (attempt.Problems.Count == 0)
                {
                    await Stage("analyze");
                    var impact = ImpactAnalyzer.Analyze(ctx.Model, attempt.Result!, attempt.Patch, output.PossibleImpacts);
                    return new PipelineResult(attempt.Patch!, attempt.Result!, ModelValidator.Validate(attempt.Result!), impact,
                        output.PossibleImpacts, output.Provider, generator.IsAi, tokens, repairs,
                        [.. notes, .. attempt.Notes], output.Note);
                }

                if (repairs >= budget.MaxRepairs)
                    throw new PipelineException("validate",
                        $"Üretilen taslak {repairs} onarım denemesinden sonra da doğrulamadan geçmedi.", attempt.Problems);

                await Stage("repair");
                var repaired = await generator.RepairAsync(task, ctx, output, attempt.Problems, token);
                if (repaired is null)
                    throw new PipelineException("validate", "Üretilen taslak doğrulamadan geçmedi ve sağlayıcı onarımı desteklemiyor.", attempt.Problems);

                repairs++;
                tokens += repaired.InputTokens + repaired.OutputTokens;
                output = repaired;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new PipelineException("timeout", $"İş {budget.EffectiveTimeout.TotalSeconds:0} sn zaman aşımı bütçesini aştı.");
        }
    }

    private static void CheckBudget(int tokens, PipelineBudget budget)
    {
        if (tokens > budget.MaxTokens)
            throw new PipelineException("budget", $"Token bütçesi aşıldı ({tokens} > {budget.MaxTokens}).");
    }

    private sealed record Attempt(ModelPatch? Patch, ArchitectureModel? Result, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes);

    private static Attempt Evaluate(GenerationTask task, GenerationContext ctx, GeneratorOutput output)
    {
        if (output.ParseProblems is { Count: > 0 })
            return new Attempt(null, null, output.ParseProblems, []);

        var resolved = DraftResolver.Resolve(ctx.Model, output.Patch);
        var (guarded, violations) = GeneratorGuard.Enforce(task, ctx.Model, resolved);

        // Yasak nesne türleri veya aşırı işlem sayısı onarım gerektirir; kaynak alıntısı düzeltmeleri yalnızca not düşer.
        var hard = violations.Where(v => v.Blocking).Select(v => v.Message).ToList();
        var soft = violations.Where(v => !v.Blocking).Select(v => v.Message).ToList();
        if (hard.Count > 0) return new Attempt(null, null, hard, soft);

        var applied = ModelPatcher.Apply(ctx.Model, guarded);
        if (!applied.Ok) return new Attempt(null, null, applied.Errors, soft);

        var blocking = ModelPatcher.NewBlockingFindings(ctx.Model, applied.Model!);
        if (blocking.Count > 0)
            return new Attempt(null, null, blocking.Select(f => $"{f.RuleId} {f.ObjectId}: {f.Message}").ToList(), soft);

        return new Attempt(guarded, applied.Model, [], soft);
    }
}
