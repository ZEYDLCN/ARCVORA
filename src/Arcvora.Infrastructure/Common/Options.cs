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

/// <summary>
/// E-posta bildirimleri (SMTP). Varsayılan KAPALI: yapılandırılmadıkça hiçbir mail kuyruğa alınmaz.
/// Parola yalnızca ortamdan/yapılandırmadan okunur; API yanıtlarında ve hata iletilerinde asla yer almaz.
/// </summary>
public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    /// <summary>"starttls" (587), "ssl" (465, doğrudan TLS) veya "none" (yalnızca güvenilir iç ağ / yerel geliştirme).</summary>
    public string Security { get; set; } = "starttls";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    /// <summary>Gönderen adresi (zorunlu).</summary>
    public string From { get; set; } = "";
    public string FromName { get; set; } = "ARCVORA Apps";
    /// <summary>Maillerdeki bağlantıların kökü (ör. https://apps.firma.com). Boşsa mailde bağlantı olmaz.</summary>
    public string PublicUrl { get; set; } = "";
    /// <summary>Şirket içi, kendinden imzalı sertifikalı posta sunucuları için. Güvenlik riski: yalnızca zorunluysa açın.</summary>
    public bool AllowInvalidCertificate { get; set; }
    public int TimeoutSeconds { get; set; } = 15;
    public int MaxAttempts { get; set; } = 6;
    public int RetryBaseSeconds { get; set; } = 60;
    public int PollIntervalMs { get; set; } = 2000;
    public int LeaseSeconds { get; set; } = 120;
    /// <summary>Onay bekleyen görev için ilk hatırlatma, sonraki hatırlatmalar arası ve en çok kaç hatırlatma.</summary>
    public int ReminderAfterHours { get; set; } = 24;
    public int ReminderRepeatHours { get; set; } = 24;
    public int MaxReminders { get; set; } = 3;
    /// <summary>Gönderilmiş/atlanmış/başarısız e-posta kayıtları (içerikte ad ve özet bulunur) bu kadar gün sonra silinir. Bekleyenlere dokunulmaz.</summary>
    public int RetentionDays { get; set; } = 90;
}

/// <summary>
/// Tek oturum açma (OpenID Connect, yetkilendirme kodu + PKCE): Microsoft Entra ID, ADFS, Keycloak, Okta, Google Workspace gibi herhangi bir OIDC sağlayıcı.
/// Varsayılan KAPALI. Yerel (şirket içi) Active Directory'ye doğrudan LDAP bağlantısı yoktur; AD, ADFS veya Entra ID üzerinden OIDC ile kullanılır.
/// </summary>
public sealed class OidcOptions
{
    public bool Enabled { get; set; }
    /// <summary>Sağlayıcının veren adresi; keşif belgesi <c>{Authority}/.well-known/openid-configuration</c> adresindedir. Üretimde https zorunludur.</summary>
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scopes { get; set; } = "openid profile email";
    /// <summary>Bu uygulamanın dışarıdan görünen kök adresi (ör. https://apps.firma.com). Dönüş adresi <c>{PublicUrl}/api/auth/oidc/callback</c> olarak sağlayıcıya kaydedilir.</summary>
    public string PublicUrl { get; set; } = "";
    public string ButtonText { get; set; } = "Şirket hesabıyla giriş";
    /// <summary>true: kimlik belirtecinde <c>email_verified=true</c> yoksa giriş reddedilir. Yalnızca kiracıya bağlı güvenilir bir sağlayıcıda (ör. Entra ID, bu talebi göndermez) kapatın.</summary>
    public bool RequireEmailVerified { get; set; } = true;
    /// <summary>true: e-postası bilinmeyen kişiler için <see cref="DefaultWorkspaceId"/> çalışma alanında "uygulama kullanıcısı" hesabı açılır. Kapalıyken yalnızca yöneticinin açtığı hesaplar girebilir.</summary>
    public bool AutoProvision { get; set; }
    public string DefaultWorkspaceId { get; set; } = "";
    /// <summary>Doluysa yalnızca bu alan adlarındaki e-postalar kabul edilir (ör. firma.com).</summary>
    public string[] AllowedEmailDomains { get; set; } = [];
    /// <summary>YALNIZCA geliştirme/test: http adresli sağlayıcıya izin verir.</summary>
    public bool AllowInsecureHttp { get; set; }
    public int TicketSeconds { get; set; } = 60;
    public int StateMinutes { get; set; } = 10;
}
