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
    // Boş 429 yerine ne olduğunu ve ne kadar beklenmesi gerektiğini söyle (arayüz title'ı olduğu gibi gösterir).
    o.OnRejected = async (context, ct) =>
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? (int)Math.Ceiling(retry.TotalSeconds) : 60;
        seconds = Math.Clamp(seconds, 1, 60);
        var res = context.HttpContext.Response;
        res.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await res.WriteAsJsonAsync(new
        {
            status = 429,
            code = "rate_limited",
            title = $"Çok fazla deneme. Lütfen {seconds} saniye sonra yeniden deneyin.",
            details = (object?)null,
        }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
    };
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
{
    builder.Services.AddHostedService<JobWorkerService>();
    builder.Services.AddHostedService<AccountingWorkerService>();
    builder.Services.AddHostedService<EmailWorkerService>();
    builder.Services.AddHostedService<MaintenanceWorkerService>();
}

// Bir ters proxy'nin (Render, Fly, Koyeb, nginx…) ARKASINDA çalışırken gerçek istemci adresi X-Forwarded-For'dan okunur; aksi halde
// tüm kullanıcılar proxy'nin tek IP'sini paylaşır ve kimlik hız sınırı herkes için ortak dolar. Varsayılan KAPALI: uygulama doğrudan
// internete açıksa başlığı istemci sahteleyebilir. Yalnızca trafik gerçekten bir proxy'den geliyorsa ForwardedHeaders__Enabled=true yapın.
if (builder.Configuration.GetValue("ForwardedHeaders:Enabled", false))
{
    builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
        // PaaS proxy adresleri önceden bilinmez; yalnızca proxy'den erişilebilir olduğu varsayılır.
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

// Minimal API, Development dışında bozuk gövdeyi sessizce gövdesiz 400'e çevirir; her ortamda aynı JSON hata biçimi için istisna fırlatılsın.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

var app = builder.Build();

if (app.Configuration.GetValue("ForwardedHeaders:Enabled", false)) app.UseForwardedHeaders();

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
            BadHttpRequestException { StatusCode: StatusCodes.Status415UnsupportedMediaType } =>
                (415, "unsupported_media_type", "İçerik türü application/json olmalı.", (object?)null),
            BadHttpRequestException => (400, "bad_request", "İstek gövdesi geçersiz.", null),
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

// Eşleşen uç noktası olmayan /api isteği arayüzün sayfasına düşmeden tutarlı JSON 404 alır (415 gibi uç noktaya özgü yanıtlara dokunulmaz).
app.Use(async (ctx, next) =>
{
    await next();
    if (ctx.Response.StatusCode == StatusCodes.Status404NotFound && !ctx.Response.HasStarted
        && ctx.GetEndpoint() is null && ctx.Request.Path.StartsWithSegments("/api"))
    {
        await ctx.Response.WriteAsJsonAsync(new { status = 404, code = "not_found", title = "Uç nokta bulunamadı." },
            (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
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
    app.UseStaticFiles(StaticSite.Options());
    // "nonfile": dosya uzantılı yollar (eksik/eski /assets/*.js) SPA'ya düşmeden 404 alır; regex: /api altı da düşmez.
    app.MapFallbackToFile("{**path:nonfile:regex(^(?!api(/|$)).*$)}", "index.html", StaticSite.Options());
}

app.Run();

public partial class Program;
