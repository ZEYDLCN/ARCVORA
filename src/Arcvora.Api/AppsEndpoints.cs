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

        // ----------------------------------------------------------------- PDF çıktısı
        secured.MapGet("/requests/{id:guid}/pdf", async (Guid id, HttpContext http, ClaimsPrincipal u, DocumentService s, CancellationToken ct) =>
        {
            var (pdf, name) = await s.RequestPdfAsync(Endpoints.UserId(u), id, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return Results.File(pdf, "application/pdf", name);
        });
        secured.MapGet("/workspaces/{id:guid}/documents/verify", async (Guid id, string? number, string? code, ClaimsPrincipal u, DocumentService s, CancellationToken ct) =>
            Results.Ok(await s.VerifyAsync(Endpoints.UserId(u), id, number, code, ct)));

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

        // ------------------------------------------------------------ arama ve raporlar (master admin)
        secured.MapGet("/workspaces/{id:guid}/requests/search", async (Guid id, string? q, string? status, Guid? appId, Guid? requesterId, string? from, string? to,
            string? page, string? pageSize, bool? includeDemo, ClaimsPrincipal u, ReportService s, CancellationToken ct) =>
            Results.Ok(await s.SearchAsync(Endpoints.UserId(u), id, q, status, appId, requesterId, from, to, page, pageSize, includeDemo ?? true, ct)));
        secured.MapGet("/workspaces/{id:guid}/reports/summary", async (Guid id, string? from, string? to, Guid? appId, string? overdueHours, bool? includeDemo, ClaimsPrincipal u, ReportService s, CancellationToken ct) =>
            Results.Ok(await s.SummaryAsync(Endpoints.UserId(u), id, from, to, appId, overdueHours, includeDemo ?? false, ct)));
        secured.MapGet("/workspaces/{id:guid}/reports/requests.csv", async (Guid id, string? q, string? status, Guid? appId, Guid? requesterId, string? from, string? to,
            bool? includeDemo, string? delimiter, ClaimsPrincipal u, ReportService s, CancellationToken ct) =>
        {
            var (data, name) = await s.ExportCsvAsync(Endpoints.UserId(u), id, q, status, appId, requesterId, from, to, includeDemo ?? true, delimiter, ct);
            return Results.File(data, "text/csv; charset=utf-8", name);
        });

        // ------------------------------------------------- denetim, saklama süresi, KVKK
        secured.MapGet("/workspaces/{id:guid}/audit", async (Guid id, string? action, Guid? actorId, string? from, string? to, string? page, string? pageSize,
            ClaimsPrincipal u, AuditViewService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(Endpoints.UserId(u), id, action, actorId, from, to, page, pageSize, ct)));
        secured.MapGet("/workspaces/{id:guid}/retention", async (Guid id, ClaimsPrincipal u, RetentionService s, CancellationToken ct) =>
            Results.Ok(await s.GetAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPut("/workspaces/{id:guid}/retention", async (Guid id, UpdateRetentionRequest r, ClaimsPrincipal u, RetentionService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(Endpoints.UserId(u), id, r, ct)));
        secured.MapPost("/workspaces/{id:guid}/retention/preview", async (Guid id, ClaimsPrincipal u, RetentionService s, CancellationToken ct) =>
            Results.Ok(await s.PreviewAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/retention/purge", async (Guid id, PurgeRequest? r, ClaimsPrincipal u, RetentionService s, CancellationToken ct) =>
            Results.Ok(await s.PurgeAsync(Endpoints.UserId(u), id, r?.Confirm, ct)));
        secured.MapDelete("/requests/{id:guid}", async (Guid id, ClaimsPrincipal u, RetentionService s, CancellationToken ct) =>
        {
            await s.DeleteRequestAsync(Endpoints.UserId(u), id, ct);
            return Results.NoContent();
        });
        secured.MapGet("/workspaces/{id:guid}/members/{userId:guid}/data-export", async (Guid id, Guid userId, HttpContext http, ClaimsPrincipal u, PersonalDataService s, CancellationToken ct) =>
        {
            var data = await s.ExportAsync(Endpoints.UserId(u), id, userId, ct);
            http.Response.Headers.CacheControl = "private, no-store";
            http.Response.Headers.ContentDisposition = "attachment; filename=\"kisisel-veri.json\"";
            return Results.Ok(data);
        });
        secured.MapGet("/workspaces/{id:guid}/former-users", async (Guid id, ClaimsPrincipal u, PersonalDataService s, CancellationToken ct) =>
            Results.Ok(await s.FormerUsersAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/members/{userId:guid}/anonymize", async (Guid id, Guid userId, ClaimsPrincipal u, PersonalDataService s, CancellationToken ct) =>
        {
            await s.AnonymizeAsync(Endpoints.UserId(u), id, userId, ct);
            return Results.Ok(new { anonymized = true });
        });

        // ----------------------------------------------------------------------- e-posta
        secured.MapGet("/workspaces/{id:guid}/email", async (Guid id, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.StatusAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPut("/workspaces/{id:guid}/email", async (Guid id, UpdateEmailSettingsRequest r, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.UpdateAsync(Endpoints.UserId(u), id, r, ct)));
        secured.MapPost("/workspaces/{id:guid}/email/test", async (Guid id, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.SendTestAsync(Endpoints.UserId(u), id, ct)));
        secured.MapGet("/workspaces/{id:guid}/email/outbox", async (Guid id, string? status, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.OutboxAsync(Endpoints.UserId(u), id, status, ct)));
        secured.MapPost("/workspaces/{id:guid}/email/outbox/{mailId:guid}/retry", async (Guid id, Guid mailId, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.RetryAsync(Endpoints.UserId(u), id, mailId, ct)));
        secured.MapGet("/me/notifications", async (ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.GetPrefsAsync(Endpoints.UserId(u), ct)));
        secured.MapPut("/me/notifications", async (UpdateNotificationPrefsRequest r, ClaimsPrincipal u, EmailAdminService s, CancellationToken ct) =>
            Results.Ok(await s.SetPrefsAsync(Endpoints.UserId(u), r, ct)));

        // ----------------------------------------------------------------------- vekâlet
        secured.MapGet("/workspaces/{id:guid}/delegations", async (Guid id, string? scope, ClaimsPrincipal u, DelegationService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(Endpoints.UserId(u), id, scope, ct)));
        secured.MapPost("/workspaces/{id:guid}/delegations", async (Guid id, CreateDelegationRequest r, ClaimsPrincipal u, DelegationService s, CancellationToken ct) =>
        {
            var d = await s.CreateAsync(Endpoints.UserId(u), id, r, ct);
            return Results.Created($"/api/workspaces/{id}/delegations", d);
        });
        secured.MapDelete("/workspaces/{id:guid}/delegations/{delegationId:guid}", async (Guid id, Guid delegationId, ClaimsPrincipal u, DelegationService s, CancellationToken ct) =>
            Results.Ok(await s.RevokeAsync(Endpoints.UserId(u), id, delegationId, ct)));
        secured.MapPost("/tasks/{id:guid}/reassign", async (Guid id, ReassignRequest r, ClaimsPrincipal u, RequestService s, CancellationToken ct) =>
            Results.Ok(await s.ReassignAsync(Endpoints.UserId(u), id, r.ToUserId, ct)));

        // ------------------------------------------------------------------ demo veri
        secured.MapGet("/workspaces/{id:guid}/demo-data", async (Guid id, ClaimsPrincipal u, DemoDataService s, CancellationToken ct) =>
            Results.Ok(await s.StatusAsync(Endpoints.UserId(u), id, ct)));
        secured.MapPost("/workspaces/{id:guid}/demo-data", async (Guid id, ClaimsPrincipal u, DemoDataService s, CancellationToken ct) =>
            Results.Created($"/api/workspaces/{id}/demo-data", await s.SeedAsync(Endpoints.UserId(u), id, ct)));
        secured.MapDelete("/workspaces/{id:guid}/demo-data", async (Guid id, ClaimsPrincipal u, DemoDataService s, CancellationToken ct) =>
        {
            await s.RemoveAsync(Endpoints.UserId(u), id, ct);
            return Results.NoContent();
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
