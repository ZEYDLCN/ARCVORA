using Arcvora.Core.Model;

namespace Arcvora.Core.Validation;

/// <summary>
/// Bulgu türleri ayrı tutulur: "inceleme önerisi" ile "şema hatası" farklı durumlardır (§15.2).
/// </summary>
public enum FindingKind
{
    Schema,
    Reference,
    Consistency,
    Traceability,
    Profile,
    /// <summary>Deterministik test değil; kural tabanlı inceleme önerisi. Kanıt değildir.</summary>
    ReviewSuggestion,
}

public enum Severity { Error, Warning, Info }

public sealed record ValidationFinding
{
    public required string RuleId { get; init; }
    public required FindingKind Kind { get; init; }
    public required Severity Severity { get; init; }
    public string? ObjectId { get; init; }
    public required string Message { get; init; }
    /// <summary>Bulgunun dayanağı (kural açıklaması veya kaynak).</summary>
    public string? Basis { get; init; }

    /// <summary>Aynı bulguyu önceki/sonraki modelde eşlemek için kararlı anahtar.</summary>
    public string Key => $"{RuleId}|{ObjectId}|{Message}";
}

public sealed record ValidationReport(IReadOnlyList<ValidationFinding> Findings)
{
    /// <summary>Şema ve referans hataları kabul edilebilir sürüm oluşmasını engeller.</summary>
    public bool HasBlockingErrors => Findings.Any(IsBlocking);

    public IEnumerable<ValidationFinding> Blocking => Findings.Where(IsBlocking);

    public static bool IsBlocking(ValidationFinding f) =>
        f.Severity == Severity.Error && f.Kind is FindingKind.Schema or FindingKind.Reference or FindingKind.Consistency;
}
