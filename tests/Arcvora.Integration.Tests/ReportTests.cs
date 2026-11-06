using System.Net;
using System.Text;
using Arcvora.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

/// <summary>G5: talep arama/sayfalama, özet rapor ve talep CSV'si.</summary>
public class ReportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Can, Api Ece, Guid Leave, Guid Expense, Guid[] Requests);

    /// <summary>İki çalışan, iki uygulama; karışık durumlarda 7 talep: 3 izin (onay/ret/bekleyen), 4 masraf (onay/iptal/2 bekleyen).</summary>
    private async Task<Kit> SetupAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Müdür Mehmet");
        var can = await admin.NewUserAsync(factory, "app_user", displayName: "Can Yıldız", managerId: boss.UserId);
        var ece = await admin.NewUserAsync(factory, "app_user", displayName: "=HYPERLINK(\"http://x\")", managerId: boss.UserId);
        var leave = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin Talebi", can.UserId, ece.UserId);
        var expense = await admin.PublishedAppAsync(AppsKit.ExpenseDef(), "Masraf", can.UserId, ece.UserId);

        async Task<Guid> Leave(Api who, string kind = "Yıllık") => (await who.SubmitAsync(leave, AppsKit.Leave(kind: kind)))["summary"].GetProperty("id").GetGuid();
        async Task<Guid> Expense(Api who, decimal amount, string desc) => (await who.SubmitAsync(expense, new { amount, description = desc }))["summary"].GetProperty("id").GetGuid();
        async Task Approve(Guid r) => await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(r)}/approve");

        var l1 = await Leave(can); await Approve(l1);
        var l2 = await Leave(can, "Mazeret"); await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(l2)}/reject", new { comment = "Yoğun" });
        var l3 = await Leave(ece);
        var e1 = await Expense(can, 1234.5m, "Müşteri yemeği"); await Approve(e1);
        var e2 = await Expense(can, 99m, "Kırtasiye"); await can.PostAsync($"/api/requests/{e2}/cancel");
        var e3 = await Expense(ece, 500m, "Taksi 100%_ücret");
        var e4 = await Expense(ece, 75m, "Ofis malzemesi");
        return new Kit(admin, boss, can, ece, leave, expense, [l1, l2, l3, e1, e2, e3, e4]);
    }

    private static string Search(Api a, string query = "") => $"/api/workspaces/{a.WorkspaceId}/requests/search{(query == "" ? "" : "?" + query)}";
    private static string Ids(Resp r) => string.Join(",", r["items"].EnumerateArray().Select(i => i.GetProperty("number").GetString()));
    private static int Total(Resp r) => r["total"].GetInt32();

    // ------------------------------------------------------------------- arama

    [Fact]
    public async Task Search_returns_a_page_with_the_total_newest_first()
    {
        var k = await SetupAsync();
        var r = await k.Admin.GetAsync(Search(k.Admin));
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        Assert.Equal(7, Total(r));
        Assert.Equal(1, r["page"].GetInt32());
        Assert.Equal(25, r["pageSize"].GetInt32());
        Assert.Equal(7, r["items"].GetArrayLength());
        var created = r["items"].EnumerateArray().Select(i => i.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(created.OrderByDescending(x => x), created);
    }

    [Fact]
    public async Task Paging_slices_consistently_and_rejects_nonsense()
    {
        var k = await SetupAsync();
        var p1 = await k.Admin.GetAsync(Search(k.Admin, "pageSize=3&page=1"));
        var p2 = await k.Admin.GetAsync(Search(k.Admin, "pageSize=3&page=2"));
        var p3 = await k.Admin.GetAsync(Search(k.Admin, "pageSize=3&page=3"));
        var p4 = await k.Admin.GetAsync(Search(k.Admin, "pageSize=3&page=4"));
        Assert.Equal([3, 3, 1, 0], new[] { p1, p2, p3, p4 }.Select(p => p["items"].GetArrayLength()).ToArray());
        Assert.All(new[] { p1, p2, p3, p4 }, p => Assert.Equal(7, Total(p)));
        var all = string.Join(",", new[] { p1, p2, p3 }.Select(Ids)).Split(',');
        Assert.Equal(7, all.Distinct().Count());                                             // sayfalar çakışmaz, eksik bırakmaz
        foreach (var bad in new[] { "page=0", "page=-1", "pageSize=0", "pageSize=101", "page=abc", "pageSize=-5" })
        {
            var r = await k.Admin.GetAsync(Search(k.Admin, bad));
            Assert.True(r.Status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest, bad + " → " + r);
        }
    }

    [Fact]
    public async Task Text_search_matches_number_summary_and_requester_name_case_insensitively()
    {
        var k = await SetupAsync();
        Assert.Equal(3, Total(await k.Admin.GetAsync(Search(k.Admin, "q=izn-"))));                // numara parçası, büyük/küçük harf fark etmez
        Assert.Equal(1, Total(await k.Admin.GetAsync(Search(k.Admin, "q=müşteri%20yemeği"))));    // özet
        Assert.Equal(4, Total(await k.Admin.GetAsync(Search(k.Admin, "q=can"))));                 // talep eden adı (Can: 4 talep)
        Assert.Equal(0, Total(await k.Admin.GetAsync(Search(k.Admin, "q=yokboyleBirSey"))));
        Assert.Equal(7, Total(await k.Admin.GetAsync(Search(k.Admin, "q=%20%20"))));              // yalnızca boşluk: süzgeç yok
    }

    [Fact]
    public async Task Wildcards_and_sql_in_the_search_text_are_plain_characters()
    {
        var k = await SetupAsync();
        Assert.Equal(1, Total(await k.Admin.GetAsync(Search(k.Admin, "q=100%25_"))));             // "100%_" aynen bulunur (Taksi 100%_ücret)
        var pct = await k.Admin.GetAsync(Search(k.Admin, "q=%25"));
        Assert.Equal(1, Total(pct));                                                              // "%" yalnızca gerçekten % içeren özet (1 adet)
        var inj = await k.Admin.GetAsync(Search(k.Admin, "q=" + Uri.EscapeDataString("'; DROP TABLE \"FlowRequests\"; --")));
        Assert.True(inj.Is(HttpStatusCode.OK), inj.ToString());
        Assert.Equal(0, Total(inj));
        Assert.Equal(7, Total(await k.Admin.GetAsync(Search(k.Admin))));                          // tablo yerinde
    }

    [Fact]
    public async Task Filters_by_status_app_requester_and_dates_combine()
    {
        var k = await SetupAsync();
        Assert.Equal(1, Total(await k.Admin.GetAsync(Search(k.Admin, "status=rejected"))));
        Assert.Equal(1, Total(await k.Admin.GetAsync(Search(k.Admin, "status=cancelled"))));
        Assert.Equal(3, Total(await k.Admin.GetAsync(Search(k.Admin, "status=in_review"))));
        Assert.Equal(3, Total(await k.Admin.GetAsync(Search(k.Admin, $"appId={k.Leave}"))));
        Assert.Equal(4, Total(await k.Admin.GetAsync(Search(k.Admin, $"appId={k.Expense}"))));
        Assert.Equal(4, Total(await k.Admin.GetAsync(Search(k.Admin, $"requesterId={k.Can.UserId}"))));
        Assert.Equal(1, Total(await k.Admin.GetAsync(Search(k.Admin, $"requesterId={k.Can.UserId}&appId={k.Leave}&status=approved"))));
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));
        Assert.Equal(7, Total(await k.Admin.GetAsync(Search(k.Admin, $"from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"))));          // bitiş günü dahil
        Assert.Equal(0, Total(await k.Admin.GetAsync(Search(k.Admin, $"to={today.AddDays(-1):yyyy-MM-dd}"))));
        Assert.Equal(0, Total(await k.Admin.GetAsync(Search(k.Admin, $"from={today.AddDays(1):yyyy-MM-dd}"))));
    }

    [Theory]
    [InlineData("status=nonsense")]
    [InlineData("from=yarin")]
    [InlineData("to=2026-13-40")]
    [InlineData("from=2026-11-10&to=2026-11-01")]
    [InlineData("appId=not-a-guid")]
    public async Task Invalid_filters_are_rejected_with_a_clear_error(string query)
    {
        var k = await SetupAsync();
        var r = await k.Admin.GetAsync(Search(k.Admin, query));
        Assert.True(r.Status is HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.NotFound, r.ToString());
    }

    [Fact]
    public async Task Search_is_for_master_admins_and_stays_inside_the_workspace()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        var other = await outsider.NewUserAsync(factory);
        Assert.True((await k.Can.GetAsync(Search(k.Admin))).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Boss.GetAsync(Search(k.Admin))).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.GetAsync(Search(k.Admin))).Is(HttpStatusCode.NotFound));
        Assert.Equal(0, Total(await outsider.GetAsync(Search(outsider))));                         // başka şirketin kayıtları sızmaz
        Assert.Equal(0, Total(await outsider.GetAsync(Search(outsider, $"appId={k.Leave}"))));
        using var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Search(k.Admin))).StatusCode);
        _ = other;
    }

    // ------------------------------------------------------------------ rapor

    private static string Report(Api a, string query = "") => $"/api/workspaces/{a.WorkspaceId}/reports/summary{(query == "" ? "" : "?" + query)}";

    [Fact]
    public async Task The_summary_counts_statuses_per_app_and_measures_durations()
    {
        var k = await SetupAsync();
        var r = await k.Admin.GetAsync(Report(k.Admin));
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        Assert.Equal(7, r["total"].GetInt32());
        var status = r["byStatus"];
        Assert.Equal(2, status.GetProperty("approved").GetInt32());
        Assert.Equal(1, status.GetProperty("rejected").GetInt32());
        Assert.Equal(1, status.GetProperty("cancelled").GetInt32());
        Assert.Equal(3, status.GetProperty("in_review").GetInt32());

        var apps = r["apps"].EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!);
        Assert.Equal(3, apps["İzin Talebi"].GetProperty("total").GetInt32());
        Assert.Equal(1, apps["İzin Talebi"].GetProperty("approved").GetInt32());
        Assert.Equal(4, apps["Masraf"].GetProperty("total").GetInt32());
        Assert.Equal(2, apps["Masraf"].GetProperty("inReview").GetInt32());
        Assert.True(apps["İzin Talebi"].GetProperty("avgHours").GetDouble() >= 0);             // karara bağlananların süresi
        Assert.False(apps["Masraf"].GetProperty("avgHours").ValueKind == System.Text.Json.JsonValueKind.Number && apps["Masraf"].GetProperty("avgHours").GetDouble() < 0);
        Assert.True(r["daily"].GetArrayLength() >= 1);
        Assert.Equal(7, r["daily"].EnumerateArray().Sum(d => d.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task The_summary_shows_each_approvers_open_workload_and_overdue_tasks()
    {
        var k = await SetupAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "ApprovalTasks" SET "CreatedAt" = now() - interval '5 days' WHERE "WorkspaceId" = {k.Admin.WorkspaceId} AND "Status" = 'Pending'""");
        }
        var r = await k.Admin.GetAsync(Report(k.Admin, "overdueHours=48"));
        var boss = r["approvers"].EnumerateArray().First(a => a.GetProperty("name").GetString() == "Müdür Mehmet");
        Assert.Equal(3, boss.GetProperty("pending").GetInt32());
        Assert.True(boss.GetProperty("oldestPendingHours").GetDouble() >= 119);
        Assert.Equal(3, boss.GetProperty("decided").GetInt32());                                // 2 onay + 1 ret
        Assert.Equal(3, r["overdue"].GetArrayLength());
        var first = r["overdue"][0];
        Assert.True(first.GetProperty("ageHours").GetDouble() >= 119);
        Assert.Equal("Müdür Mehmet", first.GetProperty("assignee").GetString());
        Assert.Matches(@"^(IZN|MSR)-", first.GetProperty("number").GetString());

        Assert.Equal(0, (await k.Admin.GetAsync(Report(k.Admin, "overdueHours=240")))["overdue"].GetArrayLength());     // eşik yükselince listede yok
    }

    [Fact]
    public async Task The_summary_excludes_demo_data_unless_asked_and_validates_its_range()
    {
        var k = await SetupAsync();
        await k.Admin.PostAsync($"/api/workspaces/{k.Admin.WorkspaceId}/demo-data");
        Assert.Equal(7, (await k.Admin.GetAsync(Report(k.Admin)))["total"].GetInt32());
        Assert.Equal(16, (await k.Admin.GetAsync(Report(k.Admin, "includeDemo=true")))["total"].GetInt32());

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(180));
        Assert.Equal(0, (await k.Admin.GetAsync(Report(k.Admin, $"from={today.AddDays(-60):yyyy-MM-dd}&to={today.AddDays(-30):yyyy-MM-dd}")))["total"].GetInt32());
        foreach (var bad in new[] { "from=2026-11-10&to=2026-11-01", $"from={today.AddDays(-400):yyyy-MM-dd}&to={today:yyyy-MM-dd}", "from=abc", "overdueHours=-1", "overdueHours=99999" })
            Assert.True((await k.Admin.GetAsync(Report(k.Admin, bad))).Is(HttpStatusCode.UnprocessableEntity), bad);
        Assert.True((await k.Can.GetAsync(Report(k.Admin))).Is(HttpStatusCode.Forbidden));
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await outsider.GetAsync(Report(k.Admin))).Is(HttpStatusCode.NotFound));
        Assert.Equal(0, (await outsider.GetAsync(Report(outsider)))["total"].GetInt32());
    }

    // ------------------------------------------------------------------- CSV

    private static string CsvUrl(Api a, string query = "") => $"/api/workspaces/{a.WorkspaceId}/reports/requests.csv{(query == "" ? "" : "?" + query)}";

    [Fact]
    public async Task The_csv_has_excel_friendly_encoding_readable_values_and_follows_the_filters()
    {
        var k = await SetupAsync();
        var (status, bytes, res) = await k.Admin.DownloadAsync(CsvUrl(k.Admin, "status=approved"));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("text/csv", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);                                              // UTF-8 BOM
        Assert.StartsWith("talepler-", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var csv = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);                                                             // başlık + 2 onaylı talep
        Assert.StartsWith("Numara;Uygulama;Durum;Talep eden;E-posta;Oluşturma (UTC);Sonuç (UTC);Süre (saat);Özet;Alanlar", lines[0]);
        Assert.Contains(lines, l => l.Contains("1.234,50 TRY"));                                   // Türkçe tutar biçimi
        Assert.Contains(lines, l => l.Contains("Onaylandı"));
        Assert.DoesNotContain(lines.Skip(1), l => l.Contains("Reddedildi"));
    }

    [Fact]
    public async Task The_csv_neutralises_spreadsheet_formulas_and_supports_comma_delimiters()
    {
        var k = await SetupAsync();
        var csv = Encoding.UTF8.GetString((await k.Admin.DownloadAsync(CsvUrl(k.Admin, $"requesterId={k.Ece.UserId}"))).Bytes);
        Assert.Contains("'=HYPERLINK", csv);                                                       // formül olarak çalışmaz
        Assert.DoesNotContain(";=HYPERLINK", csv);
        var comma = Encoding.UTF8.GetString((await k.Admin.DownloadAsync(CsvUrl(k.Admin, "delimiter=comma&status=cancelled"))).Bytes);
        Assert.StartsWith("﻿Numara,Uygulama,", comma);
        Assert.True((await k.Admin.GetAsync(CsvUrl(k.Admin, "delimiter=tab"))).Is(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task The_csv_is_admin_only_and_workspace_scoped()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await k.Can.DownloadAsync(CsvUrl(k.Admin))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.DownloadAsync(CsvUrl(k.Admin))).Status);
        var own = Encoding.UTF8.GetString((await outsider.DownloadAsync(CsvUrl(outsider))).Bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(own);                                                                         // yalnızca başlık
    }
}
