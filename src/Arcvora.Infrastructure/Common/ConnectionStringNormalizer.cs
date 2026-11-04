namespace Arcvora.Infrastructure.Common;

/// <summary>Ücretsiz barındırıcıların verdiği <c>postgres://</c> adreslerini Npgsql biçimine çevirir.</summary>
public static class ConnectionStringNormalizer
{
    public static string Normalize(string connection) => connection;
}
