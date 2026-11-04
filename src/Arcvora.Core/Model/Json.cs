using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcvora.Core.Model;

/// <summary>Model ve API için tek JSON sözleşmesi: camelCase alanlar, snake_case enum değerleri.</summary>
public static class ArcvoraJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false, strict: false);

    /// <summary>Bilinmeyen alanları reddeder; LLM çıktısını doğrularken kullanılır.</summary>
    public static JsonSerializerOptions Strict { get; } = Create(indented: false, strict: true);

    public static JsonSerializerOptions Indented { get; } = Create(indented: true, strict: false);

    private static JsonSerializerOptions Create(bool indented, bool strict)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = strict ? JsonUnmappedMemberHandling.Disallow : JsonUnmappedMemberHandling.Skip,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        return o;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("JSON boş.");
}

public static class ModelHasher
{
    /// <summary>Normalleştirilmiş modelin SHA-256 özeti (küçük harf hex).</summary>
    public static string Hash(ArchitectureModel model)
    {
        var json = ArcvoraJson.Serialize(ModelNormalizer.Normalize(model));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
