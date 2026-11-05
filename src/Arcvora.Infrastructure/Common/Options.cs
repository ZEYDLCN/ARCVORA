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

/// <summary>Muhasebe webhook işçisi (onaylanan taleplerin dış sisteme aktarımı).</summary>
public sealed class AccountingOptions
{
    public int MaxAttempts { get; set; } = 8;
    /// <summary>Yeniden deneme beklemesi = taban * 2^(deneme-1) saniye (en fazla 6 saat).</summary>
    public int RetryBaseSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 15;
    public int PollIntervalMs { get; set; } = 2000;
    /// <summary>Bir kayıt alındıktan sonra işçi çökerse yeniden denenmeden önce beklenen süre.</summary>
    public int LeaseSeconds { get; set; } = 120;
}

public sealed class AuthOptions
{
    /// <summary>
    /// false: yeni hesap açılamaz (403 registration_closed); mevcut kullanıcılar girmeye devam eder. Herkese açık ücretsiz bir dağıtımda
    /// yabancıların kayıt olup kaynakları (özellikle ücretli bir AI anahtarını) kullanmasını önlemek için ilk hesap açıldıktan sonra kapatın.
    /// </summary>
    public bool RegistrationOpen { get; set; } = true;
}


/// <summary>Talep ekleri (fiş, belge, tarama).</summary>
public sealed class AttachmentOptions
{
    /// <summary>"database" (varsayılan; baytlar PostgreSQL'de) veya "disk" (Path altında).</summary>
    public string Store { get; set; } = "database";
    /// <summary>Store=disk için kök klasör. Yedeklemeyi ve disk alanını yönetmek size kalır.</summary>
    public string Path { get; set; } = "";
    /// <summary>Tek dosya için üst sınır (1–25 MB).</summary>
    public int MaxSizeMb { get; set; } = 10;
    /// <summary>Bir kullanıcının talebe bağlanmamış (hazırlanmış) en fazla dosya sayısı; yükleme kötüye kullanımını sınırlar.</summary>
    public int MaxStagedPerUser { get; set; } = 20;
    /// <summary>Talebe bağlanmayan dosyalar bu süre sonra temizlenir.</summary>
    public int StagedTtlHours { get; set; } = 24;
}

/// <summary>Arka plan bakım işleri (hazırlanmış ek temizliği, hatırlatmalar, saklama süresi).</summary>
public sealed class MaintenanceOptions
{
    /// <summary>Bakım turlarının aralığı (dakika).</summary>
    public int IntervalMinutes { get; set; } = 10;
}

/// <summary>Yazdırılabilir PDF çıktısı.</summary>
public sealed class DocumentOptions
{
    /// <summary>PDF'e gömülecek tek bir görselin en büyük boyutu; fazlası yalnızca listelenir.</summary>
    public int MaxImageBytes { get; set; } = 4 * 1024 * 1024;
}

/// <summary>Yerel zaman: vekâlet günleri, PDF saatleri ve hatırlatmalar çalışma alanının yerel gününe göre hesaplanır.</summary>
public sealed class LocaleOptions
{
    /// <summary>UTC'ye göre sabit fark (dakika; Türkiye UTC+3 = 180). Saat dilimi veritabanı gerektirmez (Docker'da da çalışır).</summary>
    public int UtcOffsetMinutes { get; set; } = 180;
}
