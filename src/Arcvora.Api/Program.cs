using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Arcvora.Api;
using Arcvora.Core.Model;
using Arcvora.Infrastructure;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- JSON: model ve API aynı sözleşmeyi paylaşır (camelCase alanlar, snake_case enum değerleri)
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});

builder.Services.AddArcvoraInfrastructure(builder.Configuration);

// ---- Kimlik doğrulama
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key en az 32 karakter olmalı (ortam değişkeni Jwt__Key ile verin).");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = TokenService.SigningKey(jwt),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
        NameClaimType = JwtRegisteredClaimNames.Sub,
    };
});
builder.Services.AddAuthorization();

// ---- Kaba kuvvet denemelerine karşı kimlik uçlarında hız sınırı
var authLimit = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

if (builder.Configuration.GetValue("Worker:InProcess", false))
    builder.Services.AddHostedService<JobWorkerService>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>().Database.MigrateAsync();
}

// ---- Hata eşleme: beklenen uygulama hataları log'lanmaz, iç ayrıntı sızdırılmaz
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (!ctx.Response.HasStarted && ex is not OperationCanceledException)
    {
        var (status, code, message, details) = ex switch
        {
            AppException a => (a.Status, a.Code, a.Message, a.Details),
            BadHttpRequestException => (400, "bad_request", "İstek gövdesi geçersiz.", (object?)null),
            JsonException => (400, "bad_request", "İstek gövdesi geçersiz JSON.", null),
            _ => (500, "internal_error", "Beklenmeyen bir hata oluştu.", null),
        };
        if (status == 500) app.Logger.LogError(ex, "İşlenmeyen hata: {Path}", ctx.Request.Path);

        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new { status, code, title = message, details });
    }
});

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    await next();
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapArcvoraApi();

// Derlenmiş web arayüzü varsa aynı süreçten sunulur (SPA).
if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
