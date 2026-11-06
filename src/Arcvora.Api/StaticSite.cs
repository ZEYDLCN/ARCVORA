using Microsoft.AspNetCore.Builder;
using Microsoft.Net.Http.Headers;

namespace Arcvora.Api;

/// <summary>
/// Derlenmiş web arayüzünün (wwwroot) önbellek ilkesi: özetli (içeriğe göre adlandırılmış) varlıklar değişmezdir ve uzun süre
/// önbelleğe alınır; kabuk (index.html), service worker ve manifest her seferinde doğrulanır, böylece yeni sürüm hemen görülür.
/// </summary>
public static class StaticSite
{
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";
    private const string Icons = "public, max-age=86400";

    /// <summary>İstek yoluna göre Cache-Control değeri. Bilinmeyen her şey güvenli tarafta (no-cache) kalır.</summary>
    public static string CacheControl(string requestPath)
    {
        if (requestPath.StartsWith("/assets/", StringComparison.Ordinal)) return Immutable;
        if (requestPath.StartsWith("/icons/", StringComparison.Ordinal)) return Icons;
        return Revalidate;
    }

    public static StaticFileOptions Options() => new()
    {
        OnPrepareResponse = ctx => ctx.Context.Response.Headers[HeaderNames.CacheControl] = CacheControl(ctx.Context.Request.Path.Value ?? "/"),
    };
}
