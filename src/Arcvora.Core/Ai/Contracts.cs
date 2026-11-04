using System.Text.Json;
using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Profiles;

namespace Arcvora.Core.Ai;

public enum GenerationTask { Extract, Architecture, Change }

public sealed record GenerationContext(ArchitectureModel Model, TechProfile Profile, string? RequestText = null);

/// <param name="Patch">Sağlayıcı-geçici kimlikler içerebilir; boru hattı gerçek kimliklere çevirir.</param>
public sealed record GeneratorOutput(
    ModelPatch Patch,
    IReadOnlyList<PossibleImpact> PossibleImpacts,
    int InputTokens,
    int OutputTokens,
    string Provider,
    string? Note = null,
    string? RawJson = null,
    IReadOnlyList<string>? ParseProblems = null);

/// <summary>
/// Öneri üreticisi. LLM yalnızca öneri üretir; kabul, yetkilendirme ve veri yazımı uygulama kurallarındadır (§15.1).
/// </summary>
public interface IArchitectureGenerator
{
    string Name { get; }

    /// <summary>Gerçek bir yapay zekâ sağlayıcısı mı? Buluşsal üreticiler false döner ve arayüzde öyle etiketlenir.</summary>
    bool IsAi { get; }

    Task<GeneratorOutput> ExtractRequirementsAsync(GenerationContext ctx, CancellationToken ct);
    Task<GeneratorOutput> ProposeArchitectureAsync(GenerationContext ctx, CancellationToken ct);
    Task<GeneratorOutput> AnalyzeChangeAsync(GenerationContext ctx, CancellationToken ct);

    /// <summary>Doğrulamadan geçmeyen çıktıyı düzeltme denemesi; desteklenmiyorsa null.</summary>
    Task<GeneratorOutput?> RepairAsync(GenerationTask task, GenerationContext ctx, GeneratorOutput previous,
        IReadOnlyList<string> problems, CancellationToken ct);
}

// ------------------------------------------------------------------- LLM

public sealed record StructuredRequest(
    string System,
    string User,
    string ToolName,
    string ToolDescription,
    JsonElement InputSchema,
    int MaxOutputTokens);

public sealed record StructuredResponse(string Json, int InputTokens, int OutputTokens, string Model);

/// <summary>Sağlayıcı arayüzü: şemalı (araç çağrısı) üretim ve sağlayıcı değişimi (§13.1).</summary>
public interface ILlmClient
{
    string ProviderName { get; }
    Task<StructuredResponse> GenerateAsync(StructuredRequest request, CancellationToken ct);
}

/// <summary>Yeniden denenebilir sağlayıcı hatası (zaman aşımı, 429, 5xx).</summary>
public class LlmTransientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Yeniden denemenin işe yaramayacağı sağlayıcı hatası (yetki, geçersiz istek, kesilmiş çıktı).</summary>
public class LlmException(string message, Exception? inner = null) : Exception(message, inner);
