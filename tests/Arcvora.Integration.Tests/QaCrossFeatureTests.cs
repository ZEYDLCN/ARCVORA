using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>QA: özelliklerin birbiriyle etkileşimi (saklama × izin, anonimleştirme × PDF/doğrulama, yarışlar × izin bakiyesi).</summary>
public class QaCrossFeatureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App);

    private static object BalanceLeave() => new
    {
        schema = 1, prefix = "IZC", summaryFields = new[] { "start", "end" },
        fields = new object[]
        {
            new { key = "start", label = "Başlangıç", type = "date", required = true },
            new { key = "end", label = "Bitiş", type = "date", required = true, notBefore = "start" },
            new { key = "days", label = "Gün", type = "number", computed = new { op = "calendar_days", from = "start", to = "end" } },
        },
        steps = new object[] { new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } } },
        completion = new { leave = new { enabled = true, daysField = "days", startField = "start" } },
    };

    private async Task<Kit> SetupAsync(int entitlement = 14)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür Mehmet");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(BalanceLeave(), "İzin", emp.UserId);
        await admin.PostAsync(Ws(admin, "/leave/entries"), new { userId = emp.UserId, year = 2027, kind = "entitlement", days = entitlement, note = "Kıdem" });
        return new Kit(admin, boss, emp, app);
    }

    private static string Ws(Api a, string rest) => $"/api/workspaces/{a.WorkspaceId}{rest}";
    private static async Task<JsonElement> Balance(Kit k) => (await k.Emp.GetAsync(Ws(k.Admin, "/leave/me?year=2027"))).Body;

    private async Task<Guid> AskAsync(Kit k, string start = "2027-03-01", string end = "2027-03-03") =>
        (await k.Emp.SubmitAsync(k.App, new { start, end }))["summary"].GetProperty("id").GetGuid();

    private async Task AgeAsync(Guid requestId, int months)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "FlowRequests" SET "CompletedAt" = now() - make_interval(months => {months}), "CreatedAt" = now() - make_interval(months => {months}) WHERE "Id" = {requestId}""");
    }

    // ================================================== saklama × izin bakiyesi

    [Fact]
    public async Task Purging_an_old_approved_leave_request_never_gives_the_days_back()
    {
        var k = await SetupAsync();
        var id = await AskAsync(k);
        Assert.True((await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(id)}/approve")).Is(HttpStatusCode.OK));
        Assert.Equal(3m, (await Balance(k)).GetProperty("used").GetDecimal());

        await AgeAsync(id, 30);
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 });
        var purged = await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true });
        Assert.True(purged.Is(HttpStatusCode.OK), purged.ToString());
        Assert.True(purged["purged"].GetInt32() >= 1);
        Assert.True((await k.Emp.GetAsync($"/api/requests/{id}")).Is(HttpStatusCode.NotFound));       // talep gerçekten silindi

        var after = await Balance(k);
        Assert.Equal(3m, after.GetProperty("used").GetDecimal());                                       // kullanılan gün geri DÖNMEZ (kayıt defteri)
        Assert.Equal(11m, after.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task Purging_a_rejected_request_keeps_its_refund_and_the_balance_whole()
    {
        var k = await SetupAsync();
        var id = await AskAsync(k);
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(id)}/reject", new { comment = "Yoğun dönem" });
        Assert.Equal(0m, (await Balance(k)).GetProperty("used").GetDecimal());
        await AgeAsync(id, 30);
        await k.Admin.PutAsync(Ws(k.Admin, "/retention"), new { closedRequestMonths = 12 });
        await k.Admin.PostAsync(Ws(k.Admin, "/retention/purge"), new { confirm = true });
        var after = await Balance(k);
        Assert.Equal(0m, after.GetProperty("used").GetDecimal());
        Assert.Equal(14m, after.GetProperty("available").GetDecimal());
    }

    // ================================================== anonimleştirme × PDF / doğrulama

    [Fact]
    public async Task After_anonymisation_the_old_request_still_prints_and_verifies_without_the_persons_name()
    {
        var k = await SetupAsync();
        var id = await AskAsync(k);
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(id)}/approve");
        var number = (await k.Admin.GetAsync($"/api/requests/{id}"))["summary"].GetProperty("number").GetString()!;
        var before = await k.Admin.DownloadAsync($"/api/requests/{id}/pdf");
        Assert.Equal(HttpStatusCode.OK, before.Status);

        await k.Admin.DeleteAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"));
        Assert.True((await k.Admin.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"))).Is(HttpStatusCode.OK));

        var pdf = await k.Admin.DownloadAsync($"/api/requests/{id}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.Status);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.Bytes, 0, 4));
        var text = PdfText(pdf.Bytes);
        if (text is not null)
        {
            Assert.DoesNotContain("Çalışan Can", text);
            Assert.Contains("Silinmiş kullanıcı", text);
        }
        // Kod yalnızca numara/durum/zaman/veriye bağlıdır: anonimleştirme basılı kâğıdı geçersiz KILMAZ.
        var code = text is null ? null : System.Text.RegularExpressions.Regex.Match(text, @"Doğrulama kodu: ([0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4})") is { Success: true } m ? m.Groups[1].Value : null;
        if (code is not null)
        {
            var ok = await k.Boss.GetAsync(Ws(k.Admin, $"/documents/verify?number={Uri.EscapeDataString(number)}&code={Uri.EscapeDataString(code)}"));
            Assert.True(ok["valid"].GetBoolean(), ok.ToString());
        }
    }

    [Fact]
    public async Task An_anonymised_person_is_never_mailed_and_cannot_sign_in()
    {
        var k = await SetupAsync();
        var email = k.Emp.Email;
        await k.Admin.DeleteAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"));
        await k.Admin.PostAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}/anonymize"));
        await factory.RunEmailAsync();
        factory.Mail.Reset();

        // Eski adrese mail üretecek bir olay: yönetici başka birine görev yeniden atar vb. değil; doğrudan anonim hesap vekil seçilemez.
        var d = await k.Boss.PostAsync(Ws(k.Admin, "/delegations"), new { toUserId = k.Emp.UserId, startsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180)).ToString("yyyy-MM-dd"), endsOn = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180)).AddDays(2).ToString("yyyy-MM-dd") });
        Assert.False(d.Is(HttpStatusCode.Created), d.ToString());                                       // üye olmayan kişiye vekâlet verilemez
        await factory.RunEmailAsync();
        Assert.Empty(factory.Mail.To(email));

        var login = await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email, password = "temporary-pass-1" });
        Assert.True(login.Is(HttpStatusCode.Unauthorized), login.ToString());
    }

    // ================================================== yarışlar × izin bakiyesi tutarlılığı

    [Fact]
    public async Task Approving_while_the_requester_cancels_leaves_the_balance_consistent_with_the_final_state()
    {
        for (var round = 0; round < 6; round++)
        {
            var k = await SetupAsync();
            var id = await AskAsync(k);
            var task = await k.Boss.MyPendingTaskAsync(id);
            var results = await Task.WhenAll(k.Boss.PostAsync($"/api/tasks/{task}/approve"), k.Emp.PostAsync($"/api/requests/{id}/cancel"));
            Assert.All(results, r => Assert.True(r.Status is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, r.ToString()));
            Assert.Contains(results, r => r.Is(HttpStatusCode.OK));                                       // en az biri kazanır, kimse 500 almaz

            var status = (await k.Admin.GetAsync($"/api/requests/{id}"))["summary"].GetProperty("status").GetString();
            var used = (await Balance(k)).GetProperty("used").GetDecimal();
            Assert.True(status is "approved" or "cancelled", status);
            Assert.Equal(status == "approved" ? 3m : 0m, used);                                           // iptal → iade; onay → kalıcı düşüm; ikisi birden OLMAZ
        }
    }

    [Fact]
    public async Task Rejecting_while_the_requester_cancels_refunds_exactly_once()
    {
        for (var round = 0; round < 6; round++)
        {
            var k = await SetupAsync();
            var id = await AskAsync(k);
            var task = await k.Boss.MyPendingTaskAsync(id);
            var results = await Task.WhenAll(k.Boss.PostAsync($"/api/tasks/{task}/reject", new { comment = "Olmaz" }), k.Emp.PostAsync($"/api/requests/{id}/cancel"));
            Assert.All(results, r => Assert.True(r.Status is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, r.ToString()));
            var after = await Balance(k);
            Assert.Equal(0m, after.GetProperty("used").GetDecimal());
            Assert.Equal(14m, after.GetProperty("available").GetDecimal());                               // çift iade olsaydı 17 olurdu
        }
    }

    [Fact]
    public async Task Cancelling_twice_in_parallel_refunds_once()
    {
        var k = await SetupAsync();
        var id = await AskAsync(k);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => k.Emp.PostAsync($"/api/requests/{id}/cancel")));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.OK)));
        Assert.All(results.Where(r => !r.Is(HttpStatusCode.OK)), r => Assert.True(r.Status is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity, r.ToString()));
        Assert.Equal(14m, (await Balance(k)).GetProperty("available").GetDecimal());
    }

    /// <summary>pdftotext varsa PDF metni; yoksa null (metin denetimleri atlanır).</summary>
    private static string? PdfText(byte[] pdf)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "arcvora_qa_" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(tmp, pdf);
            using var p = Process.Start(new ProcessStartInfo("pdftotext", $"-layout \"{tmp}\" -") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            if (p is null) return null;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15_000);
            return p.ExitCode == 0 ? text : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
        finally { try { File.Delete(tmp); } catch (IOException) { /* geçici dosya */ } }
    }
}
