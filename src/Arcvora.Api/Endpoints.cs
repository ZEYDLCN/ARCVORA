using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Arcvora.Core.Profiles;
using Arcvora.Infrastructure.Auth;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Persistence;
using Arcvora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Arcvora.Api;

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record ChangeRequestBody(string? RequestText);
public sealed record SampleBody(Guid WorkspaceId);
public sealed record RevertBody(int? BaseVersion);

public static class Endpoints
{
    private static Guid UserId(ClaimsPrincipal u) =>
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
                : Results.Ok(new { user = new UserSummary(user.Id, user.Email, user.DisplayName), workspaces = await s.WorkspacesAsync(id, ct) });
        });

        // ----------------------------------------------------- çalışma alanı üyeleri
        secured.MapGet("/workspaces/{id:guid}/members", async (Guid id, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/members", async (Guid id, AddMemberRequest r, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
            Results.Ok(await s.UpsertAsync(UserId(u), id, r, ct)));
        secured.MapDelete("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, ClaimsPrincipal u, MemberService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(UserId(u), id, userId, ct);
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
