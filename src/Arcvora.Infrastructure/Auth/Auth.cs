using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Arcvora.Infrastructure.Auth;

public sealed class JwtOptions
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "arcvora";
    public string Audience { get; set; } = "arcvora-web";
    public int ExpiryMinutes { get; set; } = 480;
}

/// <summary>PBKDF2-SHA256; biçim: pbkdf2$iterasyon$tuz$özet (base64).</summary>
public sealed class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iter)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));

    public string Create(User user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow();
        var token = new JwtSecurityToken(
            o.Issuer, o.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.DisplayName),
            ],
            notBefore: now.UtcDateTime, expires: now.AddMinutes(o.ExpiryMinutes).UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public sealed record WorkspaceSummary(Guid Id, string Name, string Role, bool CanAccept);
public sealed record UserSummary(Guid Id, string Email, string DisplayName);
public sealed record AuthResult(string Token, UserSummary User, IReadOnlyList<WorkspaceSummary> Workspaces);

public sealed class AuthService(ArcvoraDbContext db, PasswordHasher hasher, TokenService tokens, TimeProvider clock, AuditLog audit, IOptions<AuthOptions> authOptions)
{
    private const int MinPasswordLength = 10;
    // Kullanıcı bulunamadığında da aynı maliyetli doğrulamayı yapmak için (zamanlama farkını azaltır).
    private static readonly string DummyHash = new PasswordHasher().Hash("dummy-password-for-timing");

    public static string NormalizeEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        try
        {
            if (e.Length is 0 or > 320 || new MailAddress(e).Address != e) throw new FormatException();
        }
        catch (FormatException)
        {
            throw new RequestValidationException("invalid_email", "Geçerli bir e-posta adresi girin.");
        }
        return e;
    }

    public async Task<AuthResult> RegisterAsync(string? email, string? password, string? displayName, CancellationToken ct)
    {
        if (!authOptions.Value.RegistrationOpen)
            throw new AppException(403, "registration_closed", "Yeni kayıtlar kapalı. Mevcut hesabınızla giriş yapabilirsiniz.");
        var normalized = NormalizeEmail(email);
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new RequestValidationException("weak_password", $"Parola en az {MinPasswordLength} karakter olmalı.");
        var name = (displayName ?? "").Trim();
        if (name.Length == 0) name = normalized.Split('@')[0];
        if (name.Length > 200) throw new RequestValidationException("invalid_name", "Ad çok uzun.");

        var now = clock.GetUtcNow();
        var user = new User { Id = Guid.NewGuid(), Email = normalized, DisplayName = name, PasswordHash = hasher.Hash(password), CreatedAt = now };
        var ws = new Workspace { Id = Guid.NewGuid(), Name = $"{name} çalışma alanı", CreatedAt = now };
        db.Users.Add(user);
        db.Workspaces.Add(ws);
        db.Memberships.Add(new Membership { WorkspaceId = ws.Id, UserId = user.Id, Role = WorkspaceRole.Admin, CanAccept = true, CreatedAt = now });
        audit.Add(ws.Id, null, user.Id, "user.registered", "user", user.Id.ToString());

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("email_taken", "Bu e-posta adresiyle zaten bir hesap var.");
        }

        return new AuthResult(tokens.Create(user), new UserSummary(user.Id, user.Email, user.DisplayName),
            [new WorkspaceSummary(ws.Id, ws.Name, "admin", true)]);
    }

    public async Task<AuthResult> LoginAsync(string? email, string? password, CancellationToken ct)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == e, ct);
        var ok = hasher.Verify(password ?? "", user?.PasswordHash ?? DummyHash);
        if (user is null || !ok) throw new AppException(401, "invalid_credentials", "E-posta veya parola hatalı.");

        return new AuthResult(tokens.Create(user), new UserSummary(user.Id, user.Email, user.DisplayName), await WorkspacesAsync(user.Id, ct));
    }

    public async Task<IReadOnlyList<WorkspaceSummary>> WorkspacesAsync(Guid userId, CancellationToken ct) =>
        (await (from m in db.Memberships.AsNoTracking()
                join w in db.Workspaces.AsNoTracking() on m.WorkspaceId equals w.Id
                where m.UserId == userId
                orderby w.Name
                select new { w.Id, w.Name, m.Role, m.CanAccept }).ToListAsync(ct))
        .Select(x => new WorkspaceSummary(x.Id, x.Name, x.Role.ToString().ToLowerInvariant(), x.Role == WorkspaceRole.Admin || x.CanAccept))
        .ToList();
}

/// <summary>Roller değişiklikleri dahil denetim kaydı (§19). SaveChanges çağıran işlemde yapılır.</summary>
public sealed class AuditLog(ArcvoraDbContext db, TimeProvider clock)
{
    public void Add(Guid workspaceId, Guid? projectId, Guid? actorId, string action, string? objectType = null,
        string? objectId = null, object? detail = null)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            WorkspaceId = workspaceId, ProjectId = projectId, ActorId = actorId, Action = action,
            ObjectType = objectType, ObjectId = objectId, At = clock.GetUtcNow(),
            DetailJson = detail is null ? null : Arcvora.Core.Model.ArcvoraJson.Serialize(detail),
        });
    }
}
