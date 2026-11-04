namespace Arcvora.Core.Profiles;

public enum ProfileStatus
{
    /// <summary>Örnek proje kümesiyle uçtan uca doğrulanmış; seçilebilir.</summary>
    Validated,
    /// <summary>Planlanmış; doğrulanmadığı için seçilemez (§11.2).</summary>
    Planned,
}

/// <summary>
/// Teknoloji profili: teknoloji bağımsız iş modelinin seçilen platformda nasıl uygulanabileceğini tanımlar (§11).
/// Profil değişimi tasarımın eşdeğer olduğu anlamına gelmez.
/// </summary>
public sealed record TechProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ProfileStatus Status { get; init; }
    /// <summary>Profil içeriği sürümü; dışa aktarma kayıtlarına yazılır.</summary>
    public required string ProfileVersion { get; init; }
    public required string SupportedVersionNote { get; init; }
    /// <summary>"pending": sürüm aralığı resmî kaynaklardan henüz doğrulanmadı (§11.2).</summary>
    public required string Verification { get; init; }
    public IReadOnlyList<string> OfficialSources { get; init; } = [];
    public IReadOnlyDictionary<string, string> Conventions { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> KnownLimits { get; init; } = [];
    /// <summary>Bu profil altında bileşen teknolojisinde görülmesi çelişki sayılan işaretler (küçük harf).</summary>
    public IReadOnlyList<string> ConflictingMarkers { get; init; } = [];
    public bool IsSelectable => Status == ProfileStatus.Validated;
}

public static class ProfileCatalog
{
    public const string AspNetCoreId = "aspnetcore";
    public const string SpringBootId = "springboot";

    public static TechProfile AspNetCore { get; } = new()
    {
        Id = AspNetCoreId,
        Name = "ASP.NET Core",
        Status = ProfileStatus.Validated,
        ProfileVersion = "1.0",
        SupportedVersionNote = "Desteklenen .NET sürüm aralığı uygulama başlangıcında resmî kaynaklardan doğrulanmalıdır.",
        Verification = "pending",
        OfficialSources =
        [
            "https://learn.microsoft.com/aspnet/core/",
            "https://learn.microsoft.com/ef/core/",
        ],
        Conventions = new Dictionary<string, string>
        {
            ["http_api"] = "Controller veya Minimal API",
            ["data_access"] = "EF Core",
            ["dependency_injection"] = "Yerleşik DI",
            ["transactions"] = "EF Core ve veri tabanı işlemleri",
            ["authorization"] = "Policy tabanlı yetkilendirme",
            ["background_work"] = "Gereksinime göre worker yaklaşımı (BackgroundService / ayrı worker süreci)",
        },
        KnownLimits =
        [
            "Profil değişimi bütün tasarımın eşdeğer olduğu anlamına gelmez; işlem davranışları ve paket bağımlılıkları yeniden değerlendirilmelidir.",
            "Bu profil çalışma zamanı doğrulaması yerine geçmez.",
        ],
        ConflictingMarkers =
        [
            "spring", "jpa", "hibernate", "django", "flask", "fastapi", "express", "nestjs", "laravel", "rails",
        ],
    };

    public static TechProfile SpringBoot { get; } = new()
    {
        Id = SpringBootId,
        Name = "Spring Boot",
        Status = ProfileStatus.Planned,
        ProfileVersion = "0.0",
        SupportedVersionNote = "Planlandı: ASP.NET Core ile aynı değerlendirme kümesinde doğrulanana kadar seçilemez.",
        Verification = "pending",
        ConflictingMarkers = ["asp.net", "aspnet", "ef core", "entity framework", ".net"],
    };

    public static IReadOnlyList<TechProfile> All { get; } = [AspNetCore, SpringBoot];

    public static TechProfile? Find(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
}
