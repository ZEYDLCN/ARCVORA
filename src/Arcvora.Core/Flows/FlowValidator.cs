namespace Arcvora.Core.Flows;

/// <summary>Akış tanımını yayınlamadan önce doğrular (kimlik/üyelik varlığı hariç; onu hizmet katmanı kontrol eder).</summary>
public static class FlowValidator
{
    public static IReadOnlyList<FlowIssue> Validate(FlowDefinition def) => [];
}
