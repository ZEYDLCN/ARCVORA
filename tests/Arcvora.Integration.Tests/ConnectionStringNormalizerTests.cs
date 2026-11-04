using Arcvora.Infrastructure.Common;
using Npgsql;

namespace Arcvora.Integration.Tests;

public class ConnectionStringNormalizerTests
{
    private static NpgsqlConnectionStringBuilder Parse(string s) => new(ConnectionStringNormalizer.Normalize(s));

    [Fact]
    public void Key_value_strings_pass_through_unchanged()
    {
        const string kv = "Host=localhost;Database=arcvora;Username=postgres;Password=postgres";
        Assert.Equal(kv, ConnectionStringNormalizer.Normalize(kv));
    }

    [Theory]
    [InlineData("postgres://u:p@db.example.com:6543/arcvora")]
    [InlineData("postgresql://u:p@db.example.com:6543/arcvora")]
    public void Both_uri_schemes_are_converted(string uri)
    {
        var b = Parse(uri);
        Assert.Equal("db.example.com", b.Host);
        Assert.Equal(6543, b.Port);
        Assert.Equal("arcvora", b.Database);
        Assert.Equal("u", b.Username);
        Assert.Equal("p", b.Password);
    }

    [Fact]
    public void Port_defaults_to_5432()
    {
        Assert.Equal(5432, Parse("postgres://u:p@h/db").Port);
    }

    [Fact]
    public void Credentials_are_percent_decoded()
    {
        var b = Parse("postgres://us%40er:p%40ss%3Aw%2Ford%23@h/db");
        Assert.Equal("us@er", b.Username);
        Assert.Equal("p@ss:w/ord#", b.Password);
    }

    [Theory]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("prefer", SslMode.Prefer)]
    [InlineData("require", SslMode.Require)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    public void Sslmode_query_parameter_maps_to_npgsql_ssl_mode(string sslmode, SslMode expected)
    {
        Assert.Equal(expected, Parse($"postgres://u:p@h/db?sslmode={sslmode}").SslMode);
    }

    [Fact]
    public void Unknown_query_parameters_are_ignored_instead_of_failing()
    {
        var b = Parse("postgres://u:p@h/db?sslmode=require&channel_binding=require&options=x");
        Assert.Equal("db", b.Database);
    }

    [Fact]
    public void Uri_without_a_database_name_is_rejected_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringNormalizer.Normalize("postgres://u:p@h"));
        Assert.Contains("veritabanı", ex.Message);
    }

    [Fact]
    public void Password_never_appears_in_error_messages()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ConnectionStringNormalizer.Normalize("postgres://u:gizli-parola@h"));
        Assert.DoesNotContain("gizli-parola", ex.Message);
    }
}
