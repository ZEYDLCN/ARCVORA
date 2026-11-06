using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Arcvora.Core.Profiles;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Arcvora.Api;

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record TicketRequest(string? Ticket);
public sealed record ChangeRequestBody(string? RequestText);
public sealed record SampleBody(Guid WorkspaceId);
public sealed record RevertBody(int? BaseVersion);

public static class Endpoints
{
    private static string SsoReturn(OidcOptions o, string query) => $"{o.PublicUrl.TrimEnd('/')}/#/sso?{query}";

    internal static Guid UserId(ClaimsPrincipal u) =>
        Guid.Parse(u.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? throw new UnauthorizedAccessException());

    public static void MapArcvoraApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        // ------------------------------------------------------------- kimlik
        var auth = api.MapGroup("/auth").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest r, AuthService s, CancellationToken ct) =>
            Results.Created("/api/me", await s.RegisterAsync(r.Email, r.Password, r.DisplayName, ct)));
        auth.MapPost("/login", async (LoginRequest r, AuthService s, CancellationToken ct) =>
            Results.Ok(await s.LoginAsync(r.Email, r.Password, ct)));

        // ---- Tek oturum açma (OIDC): kapalıysa tüm uç noktalar 404. Başarısızlıkta tarayıcı JSON değil #/sso?error=… sayfasına döner.
        auth.MapGet("/providers", (IOptions<OidcOptions> o) =>
            Results.Ok(new { oidc = o.Value.Enabled, oidcLabel = o.Value.Enabled ? o.Value.ButtonText : null }));
        auth.MapGet("/oidc/login", async (HttpContext http, IOptions<OidcOptions> o, OidcService s, CancellationToken ct) =>
        {
            if (!o.Value.Enabled) return Results.NotFound(new { status = 404, code = "sso_disabled", title = "Tek oturum açma kapalı." });
            OidcStart start;
            try { start = await s.StartAsync(ct); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
            {
                http.RequestServices.GetRequiredService<ILogger<OidcService>>().LogError(ex, "OIDC keşif belgesi alınamadı.");
                return Results.Redirect(SsoReturn(o.Value, "error=sso_failed"));
            }
            http.Response.Cookies.Append("arcvora_oidc", start.StateCookie, new CookieOptions
            {
                HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = http.Request.IsHttps, Path = "/api/auth/oidc", MaxAge = TimeSpan.FromMinutes(o.Value.StateMinutes),
            });
            return Results.Redirect(start.Url);
        });
        auth.MapGet("/oidc/callback", async (HttpContext http, string? code, string? state, string? error, IOptions<OidcOptions> o, OidcService s, CancellationToken ct) =>
        {
            if (!o.Value.Enabled) return Results.NotFound(new { status = 404, code = "sso_disabled", title = "Tek oturum açma kapalı." });
            var cookie = http.Request.Cookies["arcvora_oidc"];
            http.Response.Cookies.Delete("arcvora_oidc", new CookieOptions { Path = "/api/auth/oidc" });           // durum çerezi tek kullanımlık
            if (!string.IsNullOrEmpty(error)) return Results.Redirect(SsoReturn(o.Value, "error=sso_denied"));
            try { return Results.Redirect(SsoReturn(o.Value, "ticket=" + Uri.EscapeDataString(await s.CompleteAsync(code, state, cookie, ct)))); }
            catch (OidcFailure f)
            {
                http.RequestServices.GetRequiredService<ILogger<OidcService>>().LogWarning("SSO reddedildi ({Code}): {Detail}", f.Code, f.Message);
                return Results.Redirect(SsoReturn(o.Value, "error=" + f.Code));
            }
        });
        auth.MapPost("/oidc/ticket", async (TicketRequest r, IOptions<OidcOptions> o, OidcService s, CancellationToken ct) =>
            o.Value.Enabled ? Results.Ok(await s.ExchangeTicketAsync(r.Ticket, ct)) : Results.NotFound(new { status = 404, code = "sso_disabled", title = "Tek oturum açma kapalı." }));

        api.MapGet("/profiles", () => Results.Ok(ProfileCatalog.All.Select(p => new
        {
            p.Id, p.Name, status = p.Status.ToString().ToLowerInvariant(), selectable = p.IsSelectable, p.ProfileVersion,
            p.SupportedVersionNote, p.Verification, p.OfficialSources, p.Conventions, p.KnownLimits,
        })));

        var secured = api.MapGroup("").RequireAuthorization();

        secured.MapGet("/me", async (ClaimsPrincipal u, ArcvoraDbContext db, AuthService s, CancellationToken ct) =>
        {
            var id = UserId(u);
            var user = await db.Users.FindAsync([id], ct);
            return user is null ? Results.Unauthorized()
                : Results.Ok(new { user = new UserSummary(user.Id, user.Email, user.DisplayName), workspaces = await s.WorkspacesAsync(id, ct), mustChangePassword = user.MustChangePassword });
        });
        secured.MapPost("/me/password", async (ChangePasswordRequest r, ClaimsPrincipal u, AuthService s, CancellationToken ct) =>
        {
            await s.ChangePasswordAsync(UserId(u), r.CurrentPassword, r.NewPassword, ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        secured.MapAppsApi();

        // ----------------------------------------------------- çalışma alanı üyeleri
        secured.MapGet("/workspaces/{id:guid}/members", async (Guid id, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/members", async (Guid id, AddMemberRequest r, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.UpsertAsync(UserId(u), id, r, ct)));
        secured.MapPost("/workspaces/{id:guid}/users", async (Guid id, CreateUserRequest r, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Created($"/api/workspaces/{id}/members", await s.CreateUserAsync(UserId(u), id, r, ct)));
        secured.MapPatch("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UpdateMemberRequest r, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(UserId(u), id, userId, r, ct)));
        secured.MapPost("/workspaces/{id:guid}/members/{userId:guid}/reset-password", async (Guid id, Guid userId, ResetPasswordRequest r, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.ResetPasswordAsync(UserId(u), id, userId, r.Password, ct))).RequireRateLimiting("auth");
        secured.MapDelete("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(UserId(u), id, userId, ct);
            return Results.NoContent();
        });

        // ---------------------------------------------------------------- gruplar
        secured.MapGet("/workspaces/{id:guid}/groups", async (Guid id, ClaimsPrincipal u, GroupService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/groups", async (Guid id, GroupNameRequest r, ClaimsPrincipal u, GroupService s, CancellationToken ct) =>
            Results.Created($"/api/workspaces/{id}/groups", await s.CreateAsync(UserId(u), id, r.Name, ct)));
        secured.MapPatch("/workspaces/{id:guid}/groups/{groupId:guid}", async (Guid id, Guid groupId, GroupNameRequest r, ClaimsPrincipal u, GroupService s, CancellationToken ct) =>
            Results.Ok(await s.RenameAsync(UserId(u), id, groupId, r.Name, ct)));
        secured.MapPut("/workspaces/{id:guid}/groups/{groupId:guid}/members", async (Guid id, Guid groupId, GroupMembersRequest r, ClaimsPrincipal u, GroupService s, CancellationToken ct) =>
            Results.Ok(await s.SetMembersAsync(UserId(u), id, groupId, r.UserIds, ct)));
        secured.MapDelete("/workspaces/{id:guid}/groups/{groupId:guid}", async (Guid id, Guid groupId, ClaimsPrincipal u, GroupService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(UserId(u), id, groupId, ct);
            return Results.NoContent();
        });

        // ------------------------------------------------------------ projeler
        secured.MapPost("/projects", async (CreateProjectRequest r, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
        {
            var d = await s.CreateAsync(UserId(u), r, ct);
            return Results.Created($"/api/projects/{d.Summary.Id}", d);
        });
        secured.MapGet("/projects", async (ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), ct)));
        secured.MapGet("/projects/{id:guid}", async (Guid id, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(UserId(u), id, ct)));
        secured.MapDelete("/projects/{id:guid}", async (Guid id, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(UserId(u), id, ct);
            return Results.NoContent();
        });
        secured.MapPost("/samples/expense-management", async (SampleBody b, ClaimsPrincipal u, SampleService sample, ProjectService p, CancellationToken ct) =>
        {
            var d = await sample.CreateExpenseSampleAsync(UserId(u), b.WorkspaceId, p, ct);
            return Results.Created($"/api/projects/{d.Summary.Id}", d);
        });

        // ------------------------------------------------- kaynak / düzenleme
        secured.MapPost("/projects/{id:guid}/sources", async (Guid id, AddSourceRequest r, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Created($"/api/projects/{id}", await s.AddSourceAsync(UserId(u), id, r, ct)));
        secured.MapPatch("/projects/{id:guid}/requirements/{requirementId}", async (Guid id, string requirementId,
            UpdateRequirementRequest r, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateRequirementAsync(UserId(u), id, requirementId, r, ct)));
        secured.MapPatch("/projects/{id:guid}/open-questions/{questionId}", async (Guid id, string questionId,
            AnswerQuestionRequest r, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.AnswerQuestionAsync(UserId(u), id, questionId, r, ct)));
        secured.MapPost("/projects/{id:guid}/patches", async (Guid id, ApplyPatchRequest r, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.ApplyPatchAsync(UserId(u), id, r, ct)));

        // ---------------------------------------------- sürümler ve görünümler
        secured.MapGet("/projects/{id:guid}/versions", async (Guid id, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.ListVersionsAsync(UserId(u), id, ct)));
        secured.MapGet("/projects/{id:guid}/versions/{version}", async (Guid id, string version, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.GetVersionAsync(UserId(u), id, await s.ResolveVersionNumberAsync(UserId(u), id, version, ct), ct)));
        secured.MapGet("/projects/{id:guid}/versions/{version}/views", async (Guid id, string version, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.GetViewsAsync(UserId(u), id, await s.ResolveVersionNumberAsync(UserId(u), id, version, ct), ct)));
        secured.MapGet("/projects/{id:guid}/versions/{version}/findings", async (Guid id, string version, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.GetFindingsAsync(UserId(u), id, await s.ResolveVersionNumberAsync(UserId(u), id, version, ct), ct)));
        secured.MapPost("/projects/{id:guid}/versions/{version}/revert", async (Guid id, string version, RevertBody? b, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.RevertAsync(UserId(u), id, await s.ResolveVersionNumberAsync(UserId(u), id, version, ct), b?.BaseVersion, ct)));

        // ------------------------------------------------------------ işler
        secured.MapPost("/projects/{id:guid}/analyses", (Guid id, ClaimsPrincipal u, JobService s, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
            Enqueue(s, UserId(u), id, JobKinds.Analysis, null, key, ct));
        secured.MapPost("/projects/{id:guid}/architecture-proposals", (Guid id, ClaimsPrincipal u, JobService s, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
            Enqueue(s, UserId(u), id, JobKinds.Architecture, null, key, ct));
        secured.MapPost("/projects/{id:guid}/change-requests", (Guid id, ChangeRequestBody b, ClaimsPrincipal u, JobService s, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
            Enqueue(s, UserId(u), id, JobKinds.Change, b.RequestText, key, ct));
        secured.MapGet("/jobs/{jobId:guid}", async (Guid jobId, ClaimsPrincipal u, JobService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(UserId(u), jobId, ct)));
        secured.MapPost("/jobs/{jobId:guid}/cancel", async (Guid jobId, ClaimsPrincipal u, JobService s, CancellationToken ct) =>
            Results.Ok(await s.CancelAsync(UserId(u), jobId, ct)));
        secured.MapGet("/projects/{id:guid}/jobs", async (Guid id, ClaimsPrincipal u, JobService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, ct)));

        // --------------------------------------------------------- öneriler
        secured.MapGet("/projects/{id:guid}/proposals", async (Guid id, string? status, ClaimsPrincipal u, ProposalService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, status, ct)));
        secured.MapGet("/projects/{id:guid}/proposals/{proposalId:guid}", async (Guid id, Guid proposalId, ClaimsPrincipal u, ProposalService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(UserId(u), id, proposalId, ct)));
        secured.MapPost("/projects/{id:guid}/proposals/{proposalId:guid}/accept", async (Guid id, Guid proposalId, AcceptRequest? r, ClaimsPrincipal u, ProposalService s, CancellationToken ct) =>
            Results.Ok(await s.AcceptAsync(UserId(u), id, proposalId, r ?? new AcceptRequest(null, null), ct)));
        secured.MapPost("/projects/{id:guid}/proposals/{proposalId:guid}/reject", async (Guid id, Guid proposalId, RejectRequest? r, ClaimsPrincipal u, ProposalService s, CancellationToken ct) =>
            Results.Ok(await s.RejectAsync(UserId(u), id, proposalId, r ?? new RejectRequest(null), ct)));

        // ------------------------------------------------ dışa aktarma / denetim
        secured.MapPost("/projects/{id:guid}/exports", async (Guid id, ExportRequest? r, ClaimsPrincipal u, ExportService s, CancellationToken ct) =>
            Results.Created($"/api/projects/{id}/exports", await s.CreateAsync(UserId(u), id, r ?? new ExportRequest(null, null), ct)));
        secured.MapGet("/projects/{id:guid}/exports", async (Guid id, ClaimsPrincipal u, ExportService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, ct)));
        secured.MapGet("/projects/{id:guid}/exports/{artifactId:guid}/download", async (Guid id, Guid artifactId, ClaimsPrincipal u, ExportService s, CancellationToken ct) =>
        {
            var a = await s.DownloadAsync(UserId(u), id, artifactId, ct);
            return Results.File(a.Data, a.ContentType, a.FileName);
        });
        secured.MapGet("/projects/{id:guid}/audit", async (Guid id, ClaimsPrincipal u, ProjectService s, CancellationToken ct) =>
            Results.Ok(await s.AuditAsync(UserId(u), id, ct)));
    }

    private static async Task<IResult> Enqueue(JobService s, Guid userId, Guid projectId, string kind, string? text, string? key, CancellationToken ct)
    {
        var r = await s.EnqueueAsync(userId, projectId, kind, text, key, ct);
        return r.Created
            ? Results.Accepted($"/api/jobs/{r.Job.Id}", r.Job)
            : Results.Ok(r.Job);      // aynı Idempotency-Key ile tekrar gönderim: mevcut iş
    }
}