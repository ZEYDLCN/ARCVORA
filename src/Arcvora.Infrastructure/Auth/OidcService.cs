using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Arcvora.Infrastructure.Auth;

/// <summary>SSO akışının beklenen hatası. <see cref="Code"/> arayüze (adres çubuğundaki #/sso?error=) taşınır; ayrıntı yalnızca günlüğe yazılır.</summary>
public sealed class OidcFailure(string code, string detail) : Exception(detail)
{
    public string Code { get; } = code;
}

public sealed record OidcStart(string Url, string StateCookie);

/// <summary>Kullanılmış bilet kimlikleri (atomik <c>TryAdd</c>; süresi dolan kayıtlar temizlenir). Tek örnek.</summary>
public sealed class OidcTicketLedger
{
    private readonly ConcurrentDictionary<string, long> _used = new();

    /// <summary>Bilet ilk kez kullanılıyorsa true; daha önce kullanılmışsa false. <paramref name="keepUntilUnix"/> sonrasında kayıt silinebilir.</summary>
    public bool TryUse(string ticketId, long keepUntilUnix, long nowUnix)
    {
        foreach (var kv in _used.Where(kv => kv.Value < nowUnix)) _used.TryRemove(kv.Key, out _);
        return _used.TryAdd(ticketId, keepUntilUnix);
    }
}

/// <summary>Sağlayıcının keşif belgesi ve imza anahtarları (önbellekli, kendiliğinden yenilenir). Tek örnek.</summary>
public sealed class OidcMetadata(IOptions<OidcOptions> options)
{
    private ConfigurationManager<OpenIdConnectConfiguration>? _manager;
    private readonly object _gate = new();

    public Task<OpenIdConnectConfiguration> GetAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _manager ??= new ConfigurationManager<OpenIdConnectConfiguration>(
                options.Value.Authority.TrimEnd('/') + "/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = !options.Value.AllowInsecureHttp });
        }
        return _manager.GetConfigurationAsync(ct);
    }
}

/// <summary>
/// OpenID Connect yetkilendirme kodu akışı + PKCE (S256). Tarayıcı yalnızca yönlendirme görür; belirteç değişimi sunucudan sunucuyadır.
/// Kimlik belirteci imza (JWKS), veren, hedef kitle, süre ve nonce açısından doğrulanır. Durum (state/nonce/PKCE) HMAC imzalı kısa ömürlü çerezdedir (sunucuda
/// oturum gerekmez). Sonuçta tarayıcıya JWT değil, imzalı, tek kullanımlık, kısa ömürlü bir "bilet" verilir; arayüz bileti POST ile oturuma çevirir (belirteç adres çubuğunda dolaşmaz).
/// </summary>
public sealed class OidcService(ArcvoraDbContext db, AuthService auth, OidcMetadata metadata, IHttpClientFactory http, OidcTicketLedger used,
    IOptions<OidcOptions> options, IOptions<JwtOptions> jwt, TimeProvider clock, AuditLog audit, ILogger<OidcService> log)
{
    public const string ClientName = "oidc";
    private OidcOptions Opt => options.Value;

    // ============================================================== 1) yönlendirme

    public async Task<OidcStart> StartAsync(CancellationToken ct)
    {
        var cfg = await metadata.GetAsync(ct);
        var state = B64(RandomNumberGenerator.GetBytes(24));
        var nonce = B64(RandomNumberGenerator.GetBytes(24));
        var verifier = B64(RandomNumberGenerator.GetBytes(32));
        var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var cookie = Sign(new StatePayload(state, nonce, verifier, clock.GetUtcNow().AddMinutes(Opt.StateMinutes).ToUnixTimeSeconds()));

        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = Opt.ClientId, ["redirect_uri"] = RedirectUri, ["scope"] = Opt.Scopes, ["state"] = state,
            ["nonce"] = nonce, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256",
        };
        var sep = cfg.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return new OidcStart(cfg.AuthorizationEndpoint + sep + string.Join('&', q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")), cookie);
    }

    public string RedirectUri => Opt.PublicUrl.TrimEnd('/') + "/api/auth/oidc/callback";

    // ================================================================ 2) geri dönüş

    /// <summary>Kodu doğrular/değiştirir, kimliği eşler ve tek kullanımlık bilet döndürür. Hata → <see cref="OidcFailure"/>.</summary>
    public async Task<string> CompleteAsync(string? code, string? state, string? stateCookie, CancellationToken ct)
    {
        var payload = Verify<StatePayload>(stateCookie) ?? throw new OidcFailure("sso_expired", "Durum çerezi yok veya geçersiz.");
        if (payload.E < clock.GetUtcNow().ToUnixTimeSeconds()) throw new OidcFailure("sso_expired", "Durum çerezinin süresi dolmuş.");
        if (string.IsNullOrEmpty(state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(payload.S)))
            throw new OidcFailure("sso_failed", "state eşleşmiyor.");
        if (string.IsNullOrWhiteSpace(code)) throw new OidcFailure("sso_failed", "Yetkilendirme kodu yok.");

        var cfg = await metadata.GetAsync(ct);
        var idToken = await ExchangeCodeAsync(cfg, code, payload.V, ct);
        var claims = await ValidateAsync(cfg, idToken, payload.N, ct);
        var user = await ResolveUserAsync(cfg.Issuer, claims, ct);
        return IssueTicket(user.Id);
    }

    private async Task<string> ExchangeCodeAsync(OpenIdConnectConfiguration cfg, string code, string verifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = RedirectUri, ["client_id"] = Opt.ClientId,
            ["client_secret"] = Opt.ClientSecret, ["code_verifier"] = verifier,
        };
        try
        {
            using var res = await http.CreateClient(ClientName).PostAsync(cfg.TokenEndpoint, new FormUrlEncodedContent(form), ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new OidcFailure("sso_failed", $"Belirteç uç noktası {(int)res.StatusCode} döndürdü.");
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id_token", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } s
                ? s : throw new OidcFailure("sso_failed", "Yanıtta id_token yok.");
        }
        catch (OidcFailure) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            throw new OidcFailure("sso_failed", $"Belirteç değişimi başarısız: {ex.GetType().Name}");
        }
    }

    private async Task<IdClaims> ValidateAsync(OpenIdConnectConfiguration cfg, string idToken, string nonce, CancellationToken ct)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = cfg.Issuer, ValidAudience = Opt.ClientId, IssuerSigningKeys = cfg.SigningKeys,
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512, SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512],
        });
        if (!result.IsValid) throw new OidcFailure("sso_failed", $"Kimlik belirteci geçersiz: {result.Exception?.GetType().Name}");
        var jwt = (JsonWebToken)result.SecurityToken;
        string? Claim(string n) => jwt.TryGetPayloadValue<string>(n, out var v) ? v : null;

        if (Claim("nonce") != nonce) throw new OidcFailure("sso_failed", "nonce eşleşmiyor.");
        var sub = Claim("sub");
        if (string.IsNullOrWhiteSpace(sub) || sub.Length > 200) throw new OidcFailure("sso_failed", "Belirtecte sub yok veya çok uzun.");
        var email = Claim("email") ?? (Opt.RequireEmailVerified ? null : Claim("preferred_username"));
        if (string.IsNullOrWhiteSpace(email)) throw new OidcFailure("sso_no_email", "Belirtecte e-posta yok.");

        var verified = jwt.TryGetPayloadValue<bool>("email_verified", out var b) ? b : jwt.TryGetPayloadValue<string>("email_verified", out var s) && bool.TryParse(s, out var bs) ? bs : (bool?)null;
        if (Opt.RequireEmailVerified && verified != true) throw new OidcFailure("sso_email_unverified", "E-posta sağlayıcıda doğrulanmamış.");
        if (verified == false) throw new OidcFailure("sso_email_unverified", "E-posta sağlayıcıda doğrulanmamış.");

        try { email = AuthService.NormalizeEmail(email); }
        catch (RequestValidationException) { throw new OidcFailure("sso_no_email", "E-posta biçimi geçersiz."); }
        if (Opt.AllowedEmailDomains.Length > 0 && !Opt.AllowedEmailDomains.Any(d => email.EndsWith("@" + d.Trim().TrimStart('@').ToLowerInvariant(), StringComparison.Ordinal)))
            throw new OidcFailure("sso_not_allowed", $"E-posta alan adı izinli değil.");
        return new IdClaims(sub, email, Claim("name"));
    }

    // =========================================================== kimlik eşleme

    private async Task<User> ResolveUserAsync(string issuer, IdClaims c, CancellationToken ct)
    {
        // 1) Kalıcı kimlik (veren + konu): e-posta değişse de aynı kişi.
        var linked = await (from i in db.UserIdentities.AsNoTracking() join u in db.Users on i.UserId equals u.Id where i.Issuer == issuer && i.Subject == c.Sub select u).FirstOrDefaultAsync(ct);
        if (linked is not null) return Allowed(linked);

        // 2) Aynı e-postalı mevcut hesap: ilk girişte kimlik bağlanır. Hesabın bu veren için başka bir kimliği varsa reddedilir (e-posta yeniden kullanımıyla ele geçirme).
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == c.Email, ct);
        if (user is not null)
        {
            Allowed(user);
            if (await db.UserIdentities.AnyAsync(i => i.UserId == user.Id && i.Issuer == issuer, ct)) throw new OidcFailure("sso_conflict", "Hesap başka bir kimliğe bağlı.");
            db.UserIdentities.Add(new UserIdentity { Id = Guid.NewGuid(), UserId = user.Id, Issuer = issuer, Subject = c.Sub, CreatedAt = clock.GetUtcNow() });
            await AuditAsync(user.Id, "sso.linked", ct);
            await SaveAsync(ct);
            return user;
        }

        // 3) Yeni kişi: yalnızca otomatik hesap açma açıksa.
        if (!Opt.AutoProvision || !Guid.TryParse(Opt.DefaultWorkspaceId, out var ws) || !await db.Workspaces.AnyAsync(w => w.Id == ws, ct))
            throw new OidcFailure("sso_not_allowed", "Bu e-posta için hesap yok ve otomatik hesap açma kapalı.");
        var now = clock.GetUtcNow();
        var name = string.IsNullOrWhiteSpace(c.Name) ? c.Email.Split('@')[0] : c.Name.Trim();
        user = new User
        {
            Id = Guid.NewGuid(), Email = c.Email, DisplayName = name.Length > 200 ? name[..200] : name, ManagedByWorkspaceId = ws, CreatedAt = now,
            PasswordHash = "disabled$" + B64(RandomNumberGenerator.GetBytes(24)),       // parolayla giriş imkânsız; yalnızca SSO
        };
        db.Users.Add(user);
        db.Memberships.Add(new Membership { WorkspaceId = ws, UserId = user.Id, Role = WorkspaceRole.AppUser, CanAccept = false, CreatedAt = now });
        db.UserIdentities.Add(new UserIdentity { Id = Guid.NewGuid(), UserId = user.Id, Issuer = issuer, Subject = c.Sub, CreatedAt = now });
        audit.Add(ws, null, user.Id, "sso.provisioned", "user", user.Id.ToString());
        await SaveAsync(ct);
        return user;
    }

    private static User Allowed(User u) => u.AnonymizedAt is null ? u : throw new OidcFailure("sso_not_allowed", "Hesap anonimleştirilmiş.");

    private async Task AuditAsync(Guid userId, string action, CancellationToken ct)
    {
        foreach (var ws in await db.Memberships.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.WorkspaceId).ToListAsync(ct))
            audit.Add(ws, null, userId, action, "user", userId.ToString());
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new OidcFailure("sso_conflict", "Eşzamanlı bir giriş aynı hesabı oluşturdu; tekrar deneyin.");
        }
    }

    // ============================================================ 3) bilet → oturum

    private string IssueTicket(Guid userId) =>
        Sign(new TicketPayload(userId, Guid.NewGuid().ToString("N"), clock.GetUtcNow().AddSeconds(Opt.TicketSeconds).ToUnixTimeSeconds()), "oidc-ticket-v1");

    public async Task<AuthResult> ExchangeTicketAsync(string? ticket, CancellationToken ct)
    {
        var p = Verify<TicketPayload>(ticket, "oidc-ticket-v1");
        if (p is null || p.E < clock.GetUtcNow().ToUnixTimeSeconds()) throw new AppException(401, "invalid_ticket", "Giriş bileti geçersiz veya süresi dolmuş. Yeniden deneyin.");
        // Tek kullanımlık: aynı bilet ikinci kez (paralel dahil) kullanılamaz. Defter bu sürecin belleğindedir; kayıt, bilet süresi kadar tutulur.
        if (!used.TryUse(p.J, p.E + 30, clock.GetUtcNow().ToUnixTimeSeconds())) throw new AppException(401, "invalid_ticket", "Bu giriş bileti zaten kullanılmış.");
        var result = await auth.SignInAsync(p.U, ct);
        log.LogInformation("SSO ile oturum açıldı: {User}", p.U);
        return result;
    }

    // =============================================================== imzalama

    private sealed record StatePayload(string S, string N, string V, long E);
    private sealed record TicketPayload(Guid U, string J, long E);
    private sealed record IdClaims(string Sub, string Email, string? Name);

    private byte[] Key(string purpose) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), Encoding.UTF8.GetBytes(purpose));

    private string Sign<T>(T payload, string purpose = "oidc-state-v1")
    {
        var body = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        return body + "." + B64(HMACSHA256.HashData(Key(purpose), Encoding.ASCII.GetBytes(body)));
    }

    private T? Verify<T>(string? token, string purpose = "oidc-state-v1") where T : class
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 2 }) return null;
        try
        {
            var mac = HMACSHA256.HashData(Key(purpose), Encoding.ASCII.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(mac, FromB64(parts[1]))) return null;
            return JsonSerializer.Deserialize<T>(FromB64(parts[0]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return null; }
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromB64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));
}
