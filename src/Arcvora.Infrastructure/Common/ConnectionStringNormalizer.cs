using Npgsql;

namespace Arcvora.Infrastructure.Common;

/// <summary>
/// Ücretsiz barındırıcıların (Neon, Supabase, Render, Koyeb…) verdiği <c>postgres://kullanici:parola@sunucu:port/veritabani?sslmode=require</c>
/// adreslerini Npgsql'in anladığı anahtar=değer biçimine çevirir. Zaten anahtar=değer olan dizeler olduğu gibi döner.
/// </summary>
public static class ConnectionStringNormalizer
{
    public static string Normalize(string connection)
    {
        if (!connection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !connection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return connection;

        // Hata iletilerine parola sızmaması için özgün dizeyi iletiye koymayız.
        if (!Uri.TryCreate(connection, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new InvalidOperationException("Veritabanı adresi (postgres://…) çözümlenemedi: sunucu adı eksik veya biçim geçersiz.");

        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (database.Length == 0)
            throw new InvalidOperationException("Veritabanı adresinde veritabanı adı yok (postgres://kullanici:parola@sunucu:5432/VERITABANI).");

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = database,
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            b.Username = Uri.UnescapeDataString(parts[0]);
            if (parts.Length > 1) b.Password = Uri.UnescapeDataString(parts[1]);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2 || !kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase)) continue; // bilinmeyen parametreler yok sayılır
            b.SslMode = Uri.UnescapeDataString(kv[1]).ToLowerInvariant() switch
            {
                "disable" => SslMode.Disable,
                "allow" or "prefer" => SslMode.Prefer,
                "require" => SslMode.Require,
                "verify-ca" => SslMode.VerifyCA,
                "verify-full" => SslMode.VerifyFull,
                var other => throw new InvalidOperationException($"Desteklenmeyen sslmode değeri '{other}'."),
            };
        }

        return b.ConnectionString;
    }
}
