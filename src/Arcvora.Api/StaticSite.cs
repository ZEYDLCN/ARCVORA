using Microsoft.AspNetCore.Builder;

namespace Arcvora.Api;

/// <summary>Derlenmiş web arayüzünün (wwwroot) önbellek ilkesi.</summary>
public static class StaticSite
{
    /// <summary>İstek yoluna göre Cache-Control değeri.</summary>
    public static string CacheControl(string requestPath) => "";

    public static StaticFileOptions Options() => new();
}
