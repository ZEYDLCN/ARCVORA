using System.Net;
using System.Security.Cryptography;
using System.Text;
using Arcvora.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>G1: fiş/belge/tarama ekleri — yükleme, bağlama, indirme, yetki, sınırlar ve temizlik.</summary>
public class AttachmentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App);

    private async Task<Kit> SetupAsync(int maxFiles = 2, bool required = false, bool accounting = false)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Yönetici");
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: "Çalışan", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.ReceiptDef(maxFiles, required, accounting), "Fişli Masraf", emp.UserId);
        return new Kit(admin, boss, emp, app);
    }

    private static Task<Resp> Submit(Api who, Guid app, object? receipt, decimal amount = 100m, string? key = null) =>
        who.SubmitAsync(app, receipt is null ? new { amount } : new { amount, receipt }, key);

    private static async Task<string> UploadOk(Api who, Guid app, byte[]? bytes = null, string name = "fis.png")
    {
        var r = await who.UploadAsync(app, bytes ?? FilesKit.Png(), name);
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return r.Str("id");
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    // ------------------------------------------------------------------ yükleme

    [Fact]
    public async Task Upload_describes_the_file_from_its_content_not_from_what_the_client_claims()
    {
        var k = await SetupAsync();
        var png = FilesKit.Png();
        var r = await k.Emp.UploadAsync(k.App, png, "Taksi Fişi.exe");
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal("Taksi Fişi.png", r.Str("fileName"));
        Assert.Equal("image/png", r.Str("contentType"));
        Assert.Equal(png.Length, r["size"].GetInt64());
        Assert.Equal(Sha(png), r.Str("sha256"));
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    public async Task Pdf_png_and_jpeg_are_accepted(string ext, string contentType)
    {
        var k = await SetupAsync();
        var bytes = ext switch { "pdf" => FilesKit.Pdf(), "png" => FilesKit.Png(), _ => FilesKit.Jpeg() };
        var r = await k.Emp.UploadAsync(k.App, bytes, "belge." + ext);
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal(contentType, r.Str("contentType"));
    }

    [Theory]
    [InlineData("html")]
    [InlineData("svg")]
    [InlineData("exe")]
    [InlineData("zip")]
    [InlineData("text")]
    public async Task Other_content_is_refused_whatever_the_name_says(string kind)
    {
        var k = await SetupAsync();
        byte[] bytes = kind switch
        {
            "html" => FilesKit.Html(),
            "svg" => Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"),
            "exe" => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF],
            "zip" => [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00],
            _ => Encoding.UTF8.GetBytes("herhangi bir metin dosyası"),
        };
        var r = await k.Emp.UploadAsync(k.App, bytes, "fis.pdf");
        Assert.True(r.Is(HttpStatusCode.UnsupportedMediaType), r.ToString());
        Assert.Equal("unsupported_file_type", r.Str("code"));
    }

    [Fact]
    public async Task An_empty_upload_is_refused()
    {
        var k = await SetupAsync();
        var r = await k.Emp.UploadAsync(k.App, []);
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("empty_file", r.Str("code"));
    }

    [Fact]
    public async Task The_size_limit_is_enforced_exactly()
    {
        var k = await SetupAsync();
        const int limit = 10 * 1024 * 1024;
        Assert.True((await k.Emp.UploadAsync(k.App, FilesKit.BigPng(limit))).Is(HttpStatusCode.Created));
        var over = await k.Emp.UploadAsync(k.App, FilesKit.BigPng(limit + 1));
        Assert.True(over.Is(HttpStatusCode.RequestEntityTooLarge), over.ToString());
        Assert.Equal("file_too_large", over.Str("code"));
    }

    [Fact]
    public async Task Upload_needs_login_app_access_a_published_app_and_workspace_membership()
    {
        var k = await SetupAsync();
        var stranger = await k.Admin.NewUserAsync(factory);                 // üye ama yetkisiz
        var outsider = await Api.RegisterAsync(factory);
        var draft = await k.Admin.CreateAppAsync(AppsKit.ReceiptDef(), "Taslak");

        var noAccess = await stranger.UploadAsync(k.App, FilesKit.Png());
        Assert.True(noAccess.Is(HttpStatusCode.Forbidden), noAccess.ToString());
        Assert.Equal("no_app_access", noAccess.Str("code"));

        Assert.True((await outsider.UploadAsync(k.App, FilesKit.Png())).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Emp.UploadAsync(Guid.NewGuid(), FilesKit.Png())).Is(HttpStatusCode.NotFound));

        var notPublished = await k.Admin.UploadAsync(draft, FilesKit.Png());
        Assert.True(notPublished.Is(HttpStatusCode.Conflict), notPublished.ToString());
        Assert.Equal("app_not_published", notPublished.Str("code"));

        using var anon = factory.CreateClient();
        using var res = await anon.PostAsync($"/api/apps/{k.App}/uploads", new ByteArrayContent(FilesKit.Png()));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Staged_uploads_are_capped_per_user_and_freed_by_deleting()
    {
        var k = await SetupAsync();
        var first = await UploadOk(k.Emp, k.App);
        for (var i = 1; i < 20; i++) await UploadOk(k.Emp, k.App);
        var over = await k.Emp.UploadAsync(k.App, FilesKit.Png());
        Assert.True(over.Is(HttpStatusCode.Conflict), over.ToString());
        Assert.Equal("too_many_staged_files", over.Str("code"));

        Assert.True((await k.Emp.DeleteAsync($"/api/attachments/{first}")).Is(HttpStatusCode.NoContent));
        Assert.True((await k.Emp.UploadAsync(k.App, FilesKit.Png())).Is(HttpStatusCode.Created));
        // Başka kullanıcının sınırı etkilenmez.
        Assert.True((await k.Admin.UploadAsync(k.App, FilesKit.Png())).Is(HttpStatusCode.Created));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd.png")]
    [InlineData("C:\\Windows\\fis.dll", "fis.png")]
    [InlineData(null, "ek.png")]
    [InlineData("", "ek.png")]
    [InlineData("   ", "ek.png")]
    public async Task Dangerous_or_missing_file_names_become_safe_ones(string? given, string expected)
    {
        var k = await SetupAsync();
        var r = await k.Emp.UploadAsync(k.App, FilesKit.Png(), given);
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal(expected, r.Str("fileName"));
    }

    [Fact]
    public async Task A_huge_file_name_does_not_break_anything()
    {
        var k = await SetupAsync();
        var r = await k.Emp.UploadAsync(k.App, FilesKit.Png(), new string('a', 3000) + ".png");
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.True(r.Str("fileName").Length <= 100);
        Assert.EndsWith(".png", r.Str("fileName"));
    }

    // ------------------------------------------------------------------- talep

    [Fact]
    public async Task A_request_carries_its_files_in_order_and_lists_them_in_the_detail()
    {
        var k = await SetupAsync();
        var png = await UploadOk(k.Emp, k.App, FilesKit.Png(), "once.png");
        var pdf = await UploadOk(k.Emp, k.App, FilesKit.Pdf(), "sonra.pdf");
        var r = await Submit(k.Emp, k.App, new[] { png, pdf });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());

        var files = r["attachments"];
        Assert.Equal(2, files.GetArrayLength());
        Assert.Equal(png, files[0].GetProperty("id").GetString());
        Assert.Equal("receipt", files[0].GetProperty("fieldKey").GetString());
        Assert.Equal("once.png", files[0].GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", files[1].GetProperty("contentType").GetString());
        Assert.Equal(new[] { png, pdf }, r["data"].GetProperty("receipt").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task A_request_without_files_has_an_empty_attachment_list()
    {
        var k = await SetupAsync();
        var r = await Submit(k.Emp, k.App, null);
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        Assert.Equal(0, r["attachments"].GetArrayLength());
    }

    [Fact]
    public async Task Required_files_and_the_per_field_maximum_are_enforced()
    {
        var required = await SetupAsync(required: true);
        var missing = await Submit(required.Emp, required.App, null);
        Assert.True(missing.Is(HttpStatusCode.UnprocessableEntity), missing.ToString());
        Assert.Contains(missing["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == "required");

        var single = await SetupAsync(maxFiles: 1);
        var a = await UploadOk(single.Emp, single.App);
        var b = await UploadOk(single.Emp, single.App);
        var many = await Submit(single.Emp, single.App, new[] { a, b });
        Assert.True(many.Is(HttpStatusCode.UnprocessableEntity), many.ToString());
        Assert.Contains(many["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == "too_many_files");
    }

    [Fact]
    public async Task Files_of_somebody_else_unknown_ids_and_other_apps_are_refused()
    {
        var k = await SetupAsync();
        var emp2 = await k.Admin.NewUserAsync(factory, managerId: k.Boss.UserId);
        await k.Admin.GrantAsync(k.App, [k.Emp.UserId, emp2.UserId]);
        var otherApp = await k.Admin.PublishedAppAsync(AppsKit.ReceiptDef(), "Başka uygulama", k.Emp.UserId);

        var mine = await UploadOk(k.Emp, k.App);
        var forOther = await UploadOk(k.Emp, otherApp);

        foreach (var bad in new[] { mine, forOther, Guid.NewGuid().ToString() })
        {
            // 'mine' emp2 tarafından, 'forOther' yanlış uygulamada, 'rastgele' hiç yok.
            var who = bad == mine ? emp2 : k.Emp;
            var r = await Submit(who, k.App, new[] { bad });
            Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), bad + " → " + r);
            Assert.Contains(r["details"].EnumerateArray(), i => i.GetProperty("code").GetString() == "invalid_file");
        }
        // Reddedilen denemeler dosyayı harcamaz: sahibi hâlâ kullanabilir.
        Assert.True((await Submit(k.Emp, k.App, new[] { mine })).Is(HttpStatusCode.Created));
    }

    [Fact]
    public async Task A_file_can_be_attached_to_only_one_request()
    {
        var k = await SetupAsync();
        var f = await UploadOk(k.Emp, k.App);
        Assert.True((await Submit(k.Emp, k.App, new[] { f })).Is(HttpStatusCode.Created));
        var again = await Submit(k.Emp, k.App, new[] { f });
        Assert.True(again.Is(HttpStatusCode.UnprocessableEntity), again.ToString());
        Assert.Equal(1, (await k.Emp.GetAsync("/api/my/requests")).Body.GetArrayLength());
    }

    [Fact]
    public async Task Two_simultaneous_submissions_of_one_file_create_exactly_one_request()
    {
        var k = await SetupAsync();
        var f = await UploadOk(k.Emp, k.App);
        var results = await Task.WhenAll(Submit(k.Emp, k.App, new[] { f }, key: "a"), Submit(k.Emp, k.App, new[] { f }, key: "b"));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.Created)));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.UnprocessableEntity)));
        var mine = await k.Emp.GetAsync("/api/my/requests");
        Assert.Equal(1, mine.Body.GetArrayLength());
    }

    [Fact]
    public async Task Repeating_a_submission_with_the_same_key_returns_the_same_request_and_keeps_one_attachment()
    {
        var k = await SetupAsync();
        var f = await UploadOk(k.Emp, k.App);
        var first = await Submit(k.Emp, k.App, new[] { f }, key: "same");
        var second = await Submit(k.Emp, k.App, new[] { f }, key: "same");
        Assert.True(first.Is(HttpStatusCode.Created) && second.Is(HttpStatusCode.OK), first + " / " + second);
        Assert.Equal(first["summary"].GetProperty("id").GetString(), second["summary"].GetProperty("id").GetString());
        Assert.Equal(1, second["attachments"].GetArrayLength());
    }

    [Fact]
    public async Task A_rejected_submission_leaves_files_unlinked_so_the_form_can_be_fixed_and_resent()
    {
        var k = await SetupAsync();
        var f = await UploadOk(k.Emp, k.App);
        var bad = await Submit(k.Emp, k.App, new[] { f }, amount: -5m);     // tutar negatif olamaz
        Assert.True(bad.Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
        Assert.True((await Submit(k.Emp, k.App, new[] { f }, amount: 5m)).Is(HttpStatusCode.Created));
    }

    // ------------------------------------------------------------------ indirme

    [Fact]
    public async Task Download_returns_the_exact_bytes_with_safe_headers()
    {
        var k = await SetupAsync();
        var pdf = FilesKit.Pdf("Ankara Kebap Fişi");
        var id = await UploadOk(k.Emp, k.App, pdf, "Kebap Fişi ğüşiöç.pdf");
        await Submit(k.Emp, k.App, new[] { id });

        var (status, bytes, res) = await k.Emp.DownloadAsync($"/api/attachments/{id}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(pdf, bytes);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("Kebap Fişi ğüşiöç.pdf", res.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task Only_people_who_can_see_the_request_can_download_its_files()
    {
        var k = await SetupAsync();
        var coworker = await k.Admin.NewUserAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        var id = await UploadOk(k.Emp, k.App);
        var req = await Submit(k.Emp, k.App, new[] { id });
        Assert.True(req.Is(HttpStatusCode.Created));

        foreach (var who in new[] { k.Emp, k.Boss, k.Admin })
            Assert.Equal(HttpStatusCode.OK, (await who.DownloadAsync($"/api/attachments/{id}")).Status);
        foreach (var who in new[] { coworker, outsider })
            Assert.Equal(HttpStatusCode.NotFound, (await who.DownloadAsync($"/api/attachments/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync($"/api/attachments/{Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync("/api/attachments/not-a-guid")).Status);

        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/attachments/{id}")).StatusCode);
    }

    [Fact]
    public async Task A_staged_file_is_private_to_its_uploader_until_the_request_is_sent()
    {
        var k = await SetupAsync();
        var id = await UploadOk(k.Emp, k.App);
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await k.Admin.DownloadAsync($"/api/attachments/{id}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await k.Boss.DownloadAsync($"/api/attachments/{id}")).Status);
    }

    [Fact]
    public async Task The_accounting_group_can_download_files_of_requests_it_processes()
    {
        var k = await SetupAsync(accounting: true);
        var accountant = await k.Admin.NewUserAsync(factory);
        var group = await k.Admin.NewGroupAsync("Muhasebe", accountant.UserId);
        Assert.True((await k.Admin.PutAsync($"/api/workspaces/{k.Admin.WorkspaceId}/accounting/settings", new { accountingGroupId = group.Str("id") })).Is(HttpStatusCode.OK));

        var id = await UploadOk(k.Emp, k.App);
        var req = await Submit(k.Emp, k.App, new[] { id });
        var task = await k.Boss.MyPendingTaskAsync(req["summary"].GetProperty("id").GetGuid());
        Assert.True((await k.Boss.PostAsync($"/api/tasks/{task}/approve")).Is(HttpStatusCode.OK));

        Assert.Equal(HttpStatusCode.OK, (await accountant.DownloadAsync($"/api/attachments/{id}")).Status);
        var entries = await k.Admin.GetAsync($"/api/workspaces/{k.Admin.WorkspaceId}/accounting/entries");
        var files = entries.Body[0].GetProperty("payload").GetProperty("attachments");
        Assert.Equal(1, files.GetArrayLength());
        Assert.Equal(id, files[0].GetProperty("id").GetString());
        Assert.Equal("receipt", files[0].GetProperty("fieldKey").GetString());
    }

    [Fact]
    public async Task The_accounting_csv_lists_file_names_not_ids()
    {
        var k = await SetupAsync(accounting: true);
        var id = await UploadOk(k.Emp, k.App, FilesKit.Pdf(), "Otel Faturası.pdf");
        var req = await Submit(k.Emp, k.App, new[] { id });
        var task = await k.Boss.MyPendingTaskAsync(req["summary"].GetProperty("id").GetGuid());
        await k.Boss.PostAsync($"/api/tasks/{task}/approve");

        var (status, bytes, _) = await k.Admin.DownloadAsync($"/api/workspaces/{k.Admin.WorkspaceId}/accounting/export.csv");
        Assert.Equal(HttpStatusCode.OK, status);
        var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("Fiş=Otel Faturası.pdf", csv);
        Assert.DoesNotContain(id, csv);
    }

    [Fact]
    public async Task Maintenance_run_purges_stale_staged_files()
    {
        var k = await SetupAsync();
        var staged = await UploadOk(k.Emp, k.App);
        var linked = await UploadOk(k.Emp, k.App);
        await Submit(k.Emp, k.App, new[] { linked });

        var result = await factory.Services.GetRequiredService<Arcvora.Infrastructure.Jobs.MaintenanceService>().RunOnceAsync(CancellationToken.None);
        Assert.True(result.StagedFilesPurged >= 1);
        Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync($"/api/attachments/{staged}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{linked}")).Status);
    }

    [Fact]
    public async Task Files_stay_available_after_the_request_is_cancelled_or_decided()
    {
        var k = await SetupAsync();
        var id = await UploadOk(k.Emp, k.App);
        var req = await Submit(k.Emp, k.App, new[] { id });
        var rid = req["summary"].GetProperty("id").GetGuid();
        Assert.True((await k.Emp.PostAsync($"/api/requests/{rid}/cancel")).Is(HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{id}")).Status);
        Assert.Equal(1, (await k.Emp.GetAsync($"/api/requests/{rid}"))["attachments"].GetArrayLength());
    }

    // ------------------------------------------------------------------- silme

    [Fact]
    public async Task Only_the_uploader_can_delete_and_only_while_unlinked()
    {
        var k = await SetupAsync();
        var staged = await UploadOk(k.Emp, k.App);
        Assert.True((await k.Admin.DeleteAsync($"/api/attachments/{staged}")).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Boss.DeleteAsync($"/api/attachments/{staged}")).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Emp.DeleteAsync($"/api/attachments/{staged}")).Is(HttpStatusCode.NoContent));
        Assert.True((await k.Emp.DeleteAsync($"/api/attachments/{staged}")).Is(HttpStatusCode.NotFound));
        Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync($"/api/attachments/{staged}")).Status);
        Assert.True((await Submit(k.Emp, k.App, new[] { staged })).Is(HttpStatusCode.UnprocessableEntity));

        var linked = await UploadOk(k.Emp, k.App);
        await Submit(k.Emp, k.App, new[] { linked });
        var del = await k.Emp.DeleteAsync($"/api/attachments/{linked}");
        Assert.True(del.Is(HttpStatusCode.Conflict), del.ToString());
        Assert.Equal("attachment_linked", del.Str("code"));
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{linked}")).Status);
    }

    // ------------------------------------------------------------------ temizlik

    [Fact]
    public async Task Purge_removes_stale_unlinked_files_but_never_linked_ones()
    {
        var k = await SetupAsync();
        var s1 = await UploadOk(k.Emp, k.App);
        var s2 = await UploadOk(k.Emp, k.App);
        var linked = await UploadOk(k.Emp, k.App);
        await Submit(k.Emp, k.App, new[] { linked });

        using var scope = factory.Services.CreateScope();
        var purged = await scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeStagedAsync(CancellationToken.None);
        Assert.True(purged >= 2, purged.ToString());       // aynı veritabanındaki diğer testlerin hazırlanmış dosyaları da temizlenir

        foreach (var gone in new[] { s1, s2 })
            Assert.Equal(HttpStatusCode.NotFound, (await k.Emp.DownloadAsync($"/api/attachments/{gone}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await k.Emp.DownloadAsync($"/api/attachments/{linked}")).Status);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeStagedAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Form_validation_rejects_a_non_list_file_value_and_bad_ids()
    {
        var k = await SetupAsync();
        foreach (var bad in new object[] { "abc", 5, new[] { "not-a-guid" } })
        {
            var r = await Submit(k.Emp, k.App, bad);
            Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        }
    }
}
