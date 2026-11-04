namespace Arcvora.Infrastructure.Common;

public sealed class AiOptions
{
    /// <summary>"heuristic" (anahtarsız, AI olmayan) veya "anthropic".</summary>
    public string Provider { get; set; } = "heuristic";
    public int MaxTokensPerJob { get; set; } = 120_000;
    public int MaxRepairs { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 300;
    public AnthropicOptions Anthropic { get; set; } = new();
}

public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Version { get; set; } = "2023-06-01";
    public int MaxOutputTokens { get; set; } = 16_000;
}

public sealed class JobOptions
{
    public int PollIntervalMs { get; set; } = 1000;
    public int LeaseSeconds { get; set; } = 60;
    public int Concurrency { get; set; } = 2;
    public int MaxAttempts { get; set; } = 3;
    public int MaxActivePerProject { get; set; } = 3;
    /// <summary>Yeniden deneme beklemesi = taban * 2^(deneme-1) saniye.</summary>
    public int RetryBaseSeconds { get; set; } = 5;
}
