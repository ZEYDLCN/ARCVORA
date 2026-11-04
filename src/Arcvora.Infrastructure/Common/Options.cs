namespace Arcvora.Infrastructure.Common;

public sealed class AiOptions
{
    /// <summary>"heuristic" (anahtarsız, AI olmayan), "anthropic" veya "local" (OpenAI uyumlu yerel sunucu: Ollama, llama.cpp, vLLM, LM Studio).</summary>
    public string Provider { get; set; } = "heuristic";
    public int MaxTokensPerJob { get; set; } = 120_000;
    public int MaxRepairs { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 300;
    public AnthropicOptions Anthropic { get; set; } = new();
    public LocalLlmOptions Local { get; set; } = new();
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

/// <summary>OpenAI uyumlu (`/v1/chat/completions`) yerel model sunucusu. Veri makineden çıkmaz.</summary>
public sealed class LocalLlmOptions
{
    /// <summary>Ollama: http://localhost:11434 · llama.cpp `llama-server`: http://localhost:8080</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";
    /// <summary>Sunucudaki model adı (örn. qwen2.5:7b-instruct veya llama-server'a verilen takma ad). Zorunlu.</summary>
    public string Model { get; set; } = "";
    /// <summary>Çoğu yerel sunucu anahtar istemez; verilirse `Authorization: Bearer` gönderilir.</summary>
    public string ApiKey { get; set; } = "";
    public int MaxOutputTokens { get; set; } = 8192;
    /// <summary>Düşük sıcaklık: yapılandırılmış çıktıda tutarlılık için.</summary>
    public double Temperature { get; set; } = 0.2;
    /// <summary>Qwen3 gibi "düşünen" modellerde isteğe `/no_think` ekler; yanıttaki `&lt;think&gt;` bloğu her durumda temizlenir.</summary>
    public bool DisableThinking { get; set; }
}
