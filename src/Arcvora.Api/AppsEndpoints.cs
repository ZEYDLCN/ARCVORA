using System.Security.Claims;
using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace Arcvora.Api;

/// <summary>Akış uygulamaları: tasarım (master admin), kullanım (yetkili kullanıcılar), onay görevleri.</summary>
public static class AppsEndpoints
{
    public static void MapAppsApi(this RouteGroupBuilder secured)
    {
        // ------------------------------------------------------------ tasarım (master admin)
        secured.MapGet("/workspaces/{id:guid}/apps", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/apps", async (Guid id, CreateAppRequest r, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
        {
            var d = await s.CreateAsync(Endpoints.UserId(u), id, r, ct);
            return Results.Created($"/api/apps/{d.Summary.Id}", d);
        });
        secured.MapGet("/apps/{id:guid}", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPut("/apps/{id:guid}", async (Guid id, UpdateAppRequest r, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(Endpoints.UserId(u), id, r, ct)));
        secured.MapPost("/apps/{id:guid}/publish", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.PublishAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/apps/{id:guid}/archive", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.SetArchivedAsync(Endpoints.UserId(u), id, true, ct)));
        secured.MapPost("/apps/{id:guid}/restore", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.SetArchivedAsync(Endpoints.UserId(u), id, false, ct)));
        secured.MapDelete("/apps/{id:guid}", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(Endpoints.UserId(u), id, ct);
            return Results.NoContent();
        });
        secured.MapGet("/apps/{id:guid}/grants", async (Guid id, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.GetGrantsAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPut("/apps/{id:guid}/grants", async (Guid id, SetGrantsRequest r, ClaimsPrincipal u, AppDesignService s, CancellationToken ct) =>
            Results.Ok(await s.SetGrantsAsync(Endpoints.UserId(u), id, r, ct)));

        // ------------------------------------------------------------------- kullanım
        secured.MapGet("/my/apps", async (ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.MyAppsAsync(Endpoints.UserId(u), ct)));
        secured.MapGet("/apps/{id:guid}/form", async (Guid id, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.FormAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/apps/{id:guid}/requests", async (Guid id, SubmitRequest r, ClaimsPrincipal u, RequestService s,
            [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        {
            var (detail, created) = await s.SubmitAsync(Endpoints.UserId(u), id, r.Data, key, ct);
            return created ? Results.Created($"/api/requests/{detail.Summary.Id}", detail) : Results.Ok(detail);
        });
        secured.MapGet("/workspaces/{id:guid}/directory", async (Guid id, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.DirectoryAsync(Endpoints.UserId(u), id, ct)));

        // --------------------------------------------------------------------- ekler
        secured.MapPost("/apps/{id:guid}/uploads", async (Guid id, string? fileName, HttpRequest req, ClaimsPrincipal u, AttachmentService s, CancellationToken ct) =>
        {
            var dto = await s.UploadAsync(Endpoints.UserId(u), id, fileName, req.Body, req.ContentLength, ct);
            return Results.Created($"/api/attachments/{dto.Id}", dto);
        });
        secured.MapGet("/attachments/{id:guid}", async (Guid id, HttpContext http, ClaimsPrincipal u, AttachmentService s, CancellationToken ct) =>
        {
            var (meta, data) = await s.DownloadAsync(Endpoints.UserId(u), id, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(data, meta.ContentType, meta.FileName);
        });
        secured.MapDelete("/attachments/{id:guid}", async (Guid id, ClaimsPrincipal u, AttachmentService s, CancellationToken ct) =>
        {
            await s.DeleteStagedAsync(Endpoints.UserId(u), id, ct);
            return Results.NoContent();
        });

        // ------------------------------------------------------------------- talepler
        secured.MapGet("/my/requests", async (string? status, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.MyRequestsAsync(Endpoints.UserId(u), status, ct)));
        secured.MapGet("/workspaces/{id:guid}/requests", async (Guid id, string? status, Guid? appId, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.AllRequestsAsync(Endpoints.UserId(u), id, status, appId, ct)));
        secured.MapGet("/requests/{id:guid}", async (Guid id, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/requests/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.CancelAsync(Endpoints.UserId(u), id, ct)));

        // ------------------------------------------------------------------ muhasebe
        secured.MapGet("/workspaces/{id:guid}/accounting/settings", async (Guid id, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
            Results.Ok(await s.GetSettingsAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPut("/workspaces/{id:guid}/accounting/settings", async (Guid id, UpdateAccountingSettingsRequest r, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateSettingsAsync(Endpoints.UserId(u), id, r, ct)));
        secured.MapGet("/workspaces/{id:guid}/accounting/entries", async (Guid id, string? status, string? from, string? to, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(Endpoints.UserId(u), id, status, from, to, ct)));
        secured.MapPost("/workspaces/{id:guid}/accounting/entries/{entryId:guid}/process", async (Guid id, Guid entryId, ProcessEntryRequest? r, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
            Results.Ok(await s.ProcessAsync(Endpoints.UserId(u), id, entryId, r?.Note, ct)));
        secured.MapPost("/workspaces/{id:guid}/accounting/entries/{entryId:guid}/retry", async (Guid id, Guid entryId, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
            Results.Ok(await s.RetryAsync(Endpoints.UserId(u), id, entryId, ct)));
        secured.MapGet("/workspaces/{id:guid}/accounting/export.csv", async (Guid id, string? status, string? from, string? to, string? delimiter, ClaimsPrincipal u, AccountingService s, CancellationToken ct) =>
        {
            var (data, name) = await s.ExportCsvAsync(Endpoints.UserId(u), id, status, from, to, delimiter, ct);
            return Results.File(data, "text/csv; charset=utf-8", name);
        });

        // --------------------------------------------------------------------- onay
        secured.MapGet("/my/tasks", async (bool? done, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.MyTasksAsync(Endpoints.UserId(u), done ?? false, ct)));
        secured.MapPost("/tasks/{id:guid}/approve", async (Guid id, DecideRequest? r, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.DecideAsync(Endpoints.UserId(u), id, ApprovalDecision.Approve, r?.Comment, ct)));
        secured.MapPost("/tasks/{id:guid}/reject", async (Guid id, DecideRequest? r, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.DecideAsync(Endpoints.UserId(u), id, ApprovalDecision.Reject, r?.Comment, ct)));
    }
}
