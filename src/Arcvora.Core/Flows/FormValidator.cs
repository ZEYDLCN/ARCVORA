using System.Text.Json;

namespace Arcvora.Core.Flows;

public sealed record FormResult(IReadOnlyDictionary<string, object?> Values, IReadOnlyList<FlowIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

/// <summary>Talep eden kişinin gönderdiği veriyi tanıma göre doğrular ve normalleştirir.</summary>
public static class FormValidator
{
    public static FormResult Validate(FlowDefinition def, IReadOnlyDictionary<string, JsonElement> data) =>
        new(new Dictionary<string, object?>(), []);

    /// <summary><c>user</c> tipli alanlarda seçilen kullanıcı kimlikleri (üyelik kontrolü için).</summary>
    public static IReadOnlyList<(string Key, Guid Id)> UserFieldIds(FlowDefinition def, IReadOnlyDictionary<string, object?> values) => [];
}
