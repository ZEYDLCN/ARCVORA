using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arcvora.Integration.Tests;

/// <summary>Sürece gömülü sahte OIDC sağlayıcısı: keşif belgesi, JWKS, belirteç uç noktası (PKCE denetimli) ve imzalı kimlik belirteci üretir.</summary>
public sealed class MockIdp : IAsyncDisposable
{
    public sealed record Grant(string Sub, string? Email, string? Name, bool? EmailVerified, string Nonce, string Challenge, string RedirectUri);

    /// <summary>Kimlik belirtecinin kasıtlı bozulabilen parçaları (negatif testler).</summary>
    public sealed class Tamper
    {
        public string? Audience; public string? Issuer; public int ExpiresInSeconds = 300; public bool SignWithOtherKey; public string? Nonce; public bool OmitNonce; public int? TokenStatus;
        public bool OmitIdToken; public string? ClientSecret; public bool EmailVerifiedAsString;
    }

    private readonly RSA _key = RSA.Create(2048);
    private readonly RSA _otherKey = RSA.Create(2048);
    private readonly string _kid = "k1";
    private readonly WebApplication _app;
    private readonly Dictionary<string, Grant> _codes = new();
    public Tamper Next { get; set; } = new();
    public string ClientId { get; } = "arcvora-test";
    public string ClientSecret { get; } = "idp-secret-value";
    public string Issuer { get; }
    public int TokenCalls;
    public List<string> VerifierMismatches { get; } = [];

    public MockIdp()
    {
        var b = WebApplication.CreateBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.ServerUrlsKey, "http://127.0.0.1:0");
        _app = b.Build();
        _app.MapGet("/.well-known/openid-configuration", (HttpContext c) => Results.Json(new
        {
            issuer = Issuer, authorization_endpoint = Issuer + "/authorize", token_endpoint = Issuer + "/token", jwks_uri = Issuer + "/jwks",
            response_types_supported = new[] { "code" }, id_token_signing_alg_values_supported = new[] { "RS256" }, subject_types_supported = new[] { "public" },
        }));
        _app.MapGet("/jwks", () =>
        {
            var p = _key.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", kid = _kid, alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
        });
        _app.MapPost("/token", async (HttpContext c) =>
        {
            Interlocked.Increment(ref TokenCalls);
            var form = await c.Request.ReadFormAsync();
            var t = Next;
            if (t.TokenStatus is { } st) return Results.StatusCode(st);
            if (form["client_id"] != ClientId || form["client_secret"] != (t.ClientSecret ?? ClientSecret)) return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            if (!_codes.Remove(form["code"].ToString(), out var g)) return Results.Json(new { error = "invalid_grant" }, statusCode: 400);        // kod tek kullanımlık
            if (form["redirect_uri"] != g.RedirectUri) return Results.Json(new { error = "invalid_grant", error_description = "redirect_uri" }, statusCode: 400);
            var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
            if (challenge != g.Challenge) { VerifierMismatches.Add(form["code_verifier"].ToString()); return Results.Json(new { error = "invalid_grant", error_description = "PKCE" }, statusCode: 400); }
            if (t.OmitIdToken) return Results.Json(new { access_token = "x", token_type = "Bearer" });
            return Results.Json(new { access_token = "at-" + Guid.NewGuid().ToString("N"), token_type = "Bearer", id_token = MakeToken(g, t) });
        });
        _app.Start();
        Issuer = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First().TrimEnd('/');
    }

    private string MakeToken(Grant g, Tamper t)
    {
        var claims = new Dictionary<string, object> { ["sub"] = g.Sub };
        if (g.Email is not null) claims["email"] = g.Email;
        if (g.Name is not null) claims["name"] = g.Name;
        if (g.EmailVerified is { } v) claims["email_verified"] = t.EmailVerifiedAsString ? v.ToString().ToLowerInvariant() : v;
        if (!t.OmitNonce) claims["nonce"] = t.Nonce ?? g.Nonce;
        var key = t.SignWithOtherKey ? _otherKey : _key;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = t.Issuer ?? Issuer, Audience = t.Audience ?? ClientId, Claims = claims, IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1), Expires = DateTime.UtcNow.AddSeconds(t.ExpiresInSeconds),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(key) { KeyId = _kid }, SecurityAlgorithms.RsaSha256),
        });
    }

    /// <summary>Kullanıcının sağlayıcıda giriş yaptığını simüle eder: ARCVORA'nın yetkilendirme adresinden state/nonce/PKCE bilgilerini okur, bir kod üretir.</summary>
    public (string Code, string State) Authorize(Uri authorizeUrl, string sub, string? email, string? name = "SSO Kişi", bool? emailVerified = true)
    {
        var q = System.Web.HttpUtility.ParseQueryString(authorizeUrl.Query);
        if (q["response_type"] != "code" || q["client_id"] != ClientId || q["code_challenge_method"] != "S256") throw new InvalidOperationException("Yetkilendirme isteği beklenen biçimde değil: " + authorizeUrl);
        var code = "code-" + Guid.NewGuid().ToString("N");
        _codes[code] = new Grant(sub, email, name, emailVerified, q["nonce"]!, q["code_challenge"]!, q["redirect_uri"]!);
        return (code, q["state"]!);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _key.Dispose();
        _otherKey.Dispose();
    }
}
