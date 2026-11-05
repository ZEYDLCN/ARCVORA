using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using PdfSharp.Pdf.IO;

namespace Arcvora.Integration.Tests;

/// <summary>G2: talep PDF'i uç noktası — yetki, içerik, doğrulama kodu.</summary>
public class RequestPdfApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App, Guid Request);

    private async Task<Kit> SetupAsync(object? receipt = null)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Şükrü Yönetici");
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: "Çağlar Çalışan", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.ReceiptDef(3), "Fişli Masraf", emp.UserId);
        var files = new List<string>();
        foreach (var (bytes, name) in new[] { (FilesKit.Png(), "taksi.png"), (FilesKit.Pdf("otel"), "otel-faturasi.pdf") })
            if (receipt is not null) files.Add((await emp.UploadAsync(app, bytes, name)).Str("id"));
        var r = await emp.SubmitAsync(app, files.Count > 0 ? new { amount = 1234.5m, receipt = files } : new { amount = 1234.5m });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return new Kit(admin, boss, emp, app, r["summary"].GetProperty("id").GetGuid());
    }

    private static string Text(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcvora-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, pdf);
            using var p = Process.Start(new ProcessStartInfo("pdftotext", $"-layout \"{path}\" -") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return text;
        }
        catch (System.ComponentModel.Win32Exception) { return ""; }        // pdftotext yok: metin denetimleri atlanır
        finally { File.Delete(path); }
    }

    private static string? Code(string text) => Regex.Match(text, @"Doğrulama kodu: ([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4})") is { Success: true } m ? m.Groups[1].Value : null;

    private async Task<(HttpStatusCode Status, byte[] Bytes, HttpResponseMessage Res)> Pdf(Api who, Guid request) => await who.DownloadAsync($"/api/requests/{request}/pdf");

    [Fact]
    public async Task The_requester_downloads_a_real_pdf_named_after_the_request()
    {
        var k = await SetupAsync();
        var (status, bytes, res) = await Pdf(k.Emp, k.Request);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("application/pdf", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Matches(@"^MSF-\d{4}-0001\.pdf$", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        Assert.Contains("no-store", res.Headers.CacheControl!.ToString());
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        using var ms = new MemoryStream(bytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 1);
    }

    [Fact]
    public async Task The_pdf_contains_the_form_the_approval_chain_and_the_timeline()
    {
        var k = await SetupAsync();
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(k.Request)}/approve", new { comment = "Uygun, onaylandı." });
        var text = Text((await Pdf(k.Emp, k.Request)).Bytes);
        if (text.Length == 0) return;
        foreach (var expected in new[] { "Fişli Masraf", "Çağlar Çalışan", "Şükrü Yönetici", "1.234,50 TRY", "Uygun, onaylandı.", "Yönetici onayı", "ONAYLANDI", "Talep onaylandı." })
            Assert.Contains(expected, text);
        Assert.Matches(@"MSF-\d{4}-0001", text);
    }

    [Fact]
    public async Task In_review_rejected_and_cancelled_requests_print_their_status()
    {
        var k = await SetupAsync();
        Assert.Contains("İNCELEMEDE", Text((await Pdf(k.Emp, k.Request)).Bytes), StringComparison.Ordinal);

        var rejected = (await k.Emp.SubmitAsync(k.App, new { amount = 10m }))["summary"].GetProperty("id").GetGuid();
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(rejected)}/reject", new { comment = "Fiş yok" });
        var t = Text((await Pdf(k.Emp, rejected)).Bytes);
        if (t.Length > 0) { Assert.Contains("REDDEDİLDİ", t); Assert.Contains("Fiş yok", t); }

        var cancelled = (await k.Emp.SubmitAsync(k.App, new { amount = 11m }))["summary"].GetProperty("id").GetGuid();
        await k.Emp.PostAsync($"/api/requests/{cancelled}/cancel");
        var c = Text((await Pdf(k.Emp, cancelled)).Bytes);
        if (c.Length > 0) Assert.Contains("İPTAL EDİLDİ", c);
    }

    [Fact]
    public async Task Only_people_who_can_see_the_request_can_print_it()
    {
        var k = await SetupAsync();
        var coworker = await k.Admin.NewUserAsync(factory);
        var outsider = await Api.RegisterAsync(factory);
        foreach (var who in new[] { k.Emp, k.Boss, k.Admin }) Assert.Equal(HttpStatusCode.OK, (await Pdf(who, k.Request)).Status);
        foreach (var who in new[] { coworker, outsider }) Assert.Equal(HttpStatusCode.NotFound, (await Pdf(who, k.Request)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Pdf(k.Emp, Guid.NewGuid())).Status);
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/requests/{k.Request}/pdf")).StatusCode);
    }

    [Fact]
    public async Task Attachments_are_listed_and_images_are_embedded()
    {
        var without = await SetupAsync();
        var with = await SetupAsync(receipt: true);
        var a = (await Pdf(without.Emp, without.Request)).Bytes;
        var b = (await Pdf(with.Emp, with.Request)).Bytes;
        Assert.True(b.Length > a.Length);
        var text = Text(b);
        if (text.Length == 0) return;
        Assert.Contains("taksi.png", text);
        Assert.Contains("otel-faturasi.pdf", text);
        Assert.Contains("SHA-256", text);
    }

    // ------------------------------------------------------------ doğrulama kodu

    private static string Url(Api a, string number, string code) =>
        $"/api/workspaces/{a.WorkspaceId}/documents/verify?number={Uri.EscapeDataString(number)}&code={Uri.EscapeDataString(code)}";

    [Fact]
    public async Task The_printed_code_verifies_while_the_request_is_unchanged_and_fails_after_it_changes()
    {
        var k = await SetupAsync();
        var number = (await k.Emp.GetAsync($"/api/requests/{k.Request}"))["summary"].GetProperty("number").GetString()!;
        var first = Text((await Pdf(k.Emp, k.Request)).Bytes);
        if (first.Length == 0) return;
        var code = Code(first)!;
        Assert.NotNull(code);
        Assert.Equal(code, Code(Text((await Pdf(k.Emp, k.Request)).Bytes)));          // aynı durum → aynı kod

        var ok = await k.Admin.GetAsync(Url(k.Admin, number, code));
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        Assert.True(ok["valid"].GetBoolean());
        Assert.Equal("in_review", ok.Str("status"));
        Assert.Equal(number, ok.Str("number"));

        // Her üye doğrulayabilir; kod küçük harf/boşlukla yazılsa da kabul edilir.
        Assert.True((await k.Boss.GetAsync(Url(k.Boss, number, " " + code.ToLowerInvariant() + " ")))["valid"].GetBoolean());

        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(k.Request)}/approve");
        var stale = await k.Admin.GetAsync(Url(k.Admin, number, code));
        Assert.False(stale["valid"].GetBoolean());                                      // eski çıktı artık güncel değil
        Assert.False(stale.Body.TryGetProperty("status", out _));                       // geçersizken ayrıntı sızdırılmaz
        var fresh = Code(Text((await Pdf(k.Emp, k.Request)).Bytes))!;
        Assert.NotEqual(code, fresh);
        var now = await k.Admin.GetAsync(Url(k.Admin, number, fresh));
        Assert.True(now["valid"].GetBoolean());
        Assert.Equal("approved", now.Str("status"));
    }

    [Fact]
    public async Task Wrong_malformed_or_foreign_codes_never_verify()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        var number = (await k.Emp.GetAsync($"/api/requests/{k.Request}"))["summary"].GetProperty("number").GetString()!;
        foreach (var bad in new[] { "0000-0000-0000", "FFFF-FFFF-FFFF", "abc", "", "'; DROP TABLE x;--", new string('A', 500) })
        {
            var r = await k.Admin.GetAsync(Url(k.Admin, number, bad));
            Assert.True(r.Is(HttpStatusCode.OK), bad + " → " + r);
            Assert.False(r["valid"].GetBoolean());
        }
        Assert.False((await k.Admin.GetAsync(Url(k.Admin, "YOK-2026-9999", "0000-0000-0000")))["valid"].GetBoolean());
        Assert.True((await outsider.GetAsync(Url(k.Admin, number, "0000-0000-0000"))).Is(HttpStatusCode.NotFound));     // başka çalışma alanı
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url(k.Admin, number, "0000-0000-0000"))).StatusCode);
    }

    [Fact]
    public async Task A_code_from_one_request_never_verifies_another()
    {
        var k = await SetupAsync();
        var other = (await k.Emp.SubmitAsync(k.App, new { amount = 1234.5m }))["summary"].GetProperty("id").GetGuid();
        var numberA = (await k.Emp.GetAsync($"/api/requests/{k.Request}"))["summary"].GetProperty("number").GetString()!;
        var codeOther = Code(Text((await Pdf(k.Emp, other)).Bytes));
        if (codeOther is null) return;
        Assert.False((await k.Admin.GetAsync(Url(k.Admin, numberA, codeOther)))["valid"].GetBoolean());
    }
}
