using System.Net;
using System.Text.Json;

namespace Arcvora.Integration.Tests;

/// <summary>G7: resmî tatil takvimi, iş günü hesabı, izin bakiyesi (rezerve/iade), kıdem hakkı ve devir.</summary>
public class LeaveBalanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid App);

    private static object LeaveDef(bool workDays = true, bool balance = true) => new
    {
        schema = 1, prefix = "IZB", summaryFields = new[] { "start", "end", "kind" },
        fields = new object[]
        {
            new { key = "start", label = "Başlangıç", type = "date", required = true },
            new { key = "end", label = "Bitiş", type = "date", required = true, notBefore = "start" },
            new { key = "days", label = "Gün", type = "number", computed = new { op = workDays ? "work_days" : "calendar_days", from = "start", to = "end" } },
            new { key = "kind", label = "Tür", type = "select", required = true, options = new[] { "Yıllık izin", "Mazeret izni" } },
        },
        steps = new object[] { new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } } },
        completion = balance ? new { leave = new { enabled = true, daysField = "days", startField = "start", typeField = "kind", countsFor = new[] { "Yıllık izin" } } } : null,
    };

    private async Task<Kit> SetupAsync(bool workDays = true, bool balance = true)
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(LeaveDef(workDays, balance), "İzin", emp.UserId);
        return new Kit(admin, boss, emp, app);
    }

    private static string Ws(Api a, string rest) => $"/api/workspaces/{a.WorkspaceId}{rest}";

    private static Task<Resp> Entry(Api admin, Api who, string kind, decimal days, int year = 2027, string? note = null) =>
        admin.PostAsync(Ws(admin, "/leave/entries"), new { userId = who.UserId, year, kind, days, note });

    private static Task<Resp> Ask(Kit k, Api who, string start = "2027-03-01", string end = "2027-03-05", string kind = "Yıllık izin", string? key = null) =>
        who.SubmitAsync(k.App, new { start, end, kind }, key);

    private static async Task<JsonElement> Me(Api who, Api ws, int year = 2027) => (await who.GetAsync(Ws(ws, $"/leave/me?year={year}"))).Body;
    private static decimal Num(JsonElement e, string name) => e.GetProperty(name).GetDecimal();

    // ================================================================== tatil takvimi

    [Fact]
    public async Task Fixed_national_holidays_can_be_seeded_once_per_year_and_include_the_half_day_eve()
    {
        var k = await SetupAsync();
        var first = await k.Admin.PostAsync(Ws(k.Admin, "/holidays/seed-fixed"), new { year = 2026 });
        Assert.True(first.Is(HttpStatusCode.OK), first.ToString());
        Assert.Equal(8, first["added"].GetInt32());
        var again = await k.Admin.PostAsync(Ws(k.Admin, "/holidays/seed-fixed"), new { year = 2026 });
        Assert.Equal(0, again["added"].GetInt32());
        Assert.Equal(8, again["skipped"].GetInt32());

        var list = await k.Emp.GetAsync(Ws(k.Admin, "/holidays?year=2026"));                      // her üye okuyabilir
        Assert.True(list.Is(HttpStatusCode.OK), list.ToString());
        Assert.Equal(8, list.Body.GetArrayLength());
        var dates = list.Body.EnumerateArray().Select(h => h.GetProperty("date").GetString()!).ToList();
        Assert.Equal(dates.Order().ToList(), dates);
        Assert.Contains("2026-01-01", dates);
        Assert.Contains("2026-10-29", dates);
        var eve = list.Body.EnumerateArray().First(h => h.GetProperty("date").GetString() == "2026-10-28");
        Assert.True(eve.GetProperty("halfDay").GetBoolean());
        Assert.Equal(0, (await k.Emp.GetAsync(Ws(k.Admin, "/holidays?year=2027"))).Body.GetArrayLength());
        foreach (var bad in new[] { 1999, 2101, 0 }) Assert.True((await k.Admin.PostAsync(Ws(k.Admin, "/holidays/seed-fixed"), new { year = bad })).Is(HttpStatusCode.UnprocessableEntity), bad.ToString());
    }

    [Fact]
    public async Task Holidays_are_added_and_removed_by_admins_with_validation()
    {
        var k = await SetupAsync();
        var ok = await k.Admin.PostAsync(Ws(k.Admin, "/holidays"), new { date = "2026-03-20", name = "Ramazan Bayramı 1. gün" });
        Assert.True(ok.Is(HttpStatusCode.Created), ok.ToString());
        Assert.False(ok["halfDay"].GetBoolean());
        var dup = await k.Admin.PostAsync(Ws(k.Admin, "/holidays"), new { date = "2026-03-20", name = "Başka" });
        Assert.True(dup.Is(HttpStatusCode.Conflict), dup.ToString());
        Assert.Equal("holiday_exists", dup.Str("code"));
        foreach (var bad in new object[] { new { date = "yarın", name = "x" }, new { date = "2026-02-30", name = "x" }, new { date = "2026-03-21", name = "  " }, new { date = "2026-03-21", name = new string('x', 101) }, new { name = "x" } })
            Assert.True((await k.Admin.PostAsync(Ws(k.Admin, "/holidays"), bad)).Is(HttpStatusCode.UnprocessableEntity), bad.ToString());

        Assert.True((await k.Emp.PostAsync(Ws(k.Admin, "/holidays"), new { date = "2026-03-22", name = "x" })).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Emp.DeleteAsync(Ws(k.Admin, "/holidays/" + ok.Str("id")))).Is(HttpStatusCode.Forbidden));
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await outsider.GetAsync(Ws(k.Admin, "/holidays"))).Is(HttpStatusCode.NotFound));
        Assert.True((await outsider.DeleteAsync(Ws(outsider, "/holidays/" + ok.Str("id")))).Is(HttpStatusCode.NotFound));
        Assert.True((await k.Admin.DeleteAsync(Ws(k.Admin, "/holidays/" + ok.Str("id")))).Is(HttpStatusCode.NoContent));
        Assert.True((await k.Admin.DeleteAsync(Ws(k.Admin, "/holidays/" + ok.Str("id")))).Is(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Work_days_exclude_holidays_and_half_days_and_the_form_ships_the_calendar()
    {
        var k = await SetupAsync(balance: false);
        // 27 Eki (Sal) → 2 Kas (Pzt) 2026: takvimsiz 5 gün
        var plain = await k.Emp.SubmitAsync(k.App, new { start = "2026-10-27", end = "2026-11-02", kind = "Yıllık izin" });
        Assert.Equal(5m, plain["data"].GetProperty("days").GetDecimal());

        await k.Admin.PostAsync(Ws(k.Admin, "/holidays/seed-fixed"), new { year = 2026 });         // 28 Eki yarım, 29 Eki tam tatil
        var form = await k.Emp.GetAsync($"/api/apps/{k.App}/form");
        Assert.Contains(form["holidays"].EnumerateArray(), h => h.GetProperty("date").GetString() == "2026-10-29" && !h.GetProperty("halfDay").GetBoolean());
        var withCal = await k.Emp.SubmitAsync(k.App, new { start = "2026-10-27", end = "2026-11-02", kind = "Yıllık izin" });
        Assert.Equal(3.5m, withCal["data"].GetProperty("days").GetDecimal());                      // Sal 1 + Çar 0,5 + Per 0 + Cum 1 + Pzt 1
        Assert.Equal(0, (await k.Emp.GetAsync($"/api/apps/{await k.Admin.PublishedAppAsync(AppsKit.LeaveDef(), "Eski", k.Emp.UserId)}/form"))["holidays"].GetArrayLength());     // takvim kullanmayan uygulamaya gönderilmez
    }

    // ================================================================== bakiye

    [Fact]
    public async Task Balances_start_at_zero_and_follow_entitlements_and_adjustments()
    {
        var k = await SetupAsync();
        var zero = await Me(k.Emp, k.Admin);
        Assert.Equal(0m, Num(zero, "available"));
        Assert.Equal(2027, zero.GetProperty("year").GetInt32());

        Assert.True((await Entry(k.Admin, k.Emp, "entitlement", 14)).Is(HttpStatusCode.Created));
        Assert.True((await Entry(k.Admin, k.Emp, "adjustment", -2, note: "Önceki yıl fazla kullanım")).Is(HttpStatusCode.Created));
        Assert.True((await Entry(k.Admin, k.Emp, "carryover", 3.5m)).Is(HttpStatusCode.Created));
        var me = await Me(k.Emp, k.Admin);
        Assert.Equal(14m, Num(me, "entitled"));
        Assert.Equal(-2m, Num(me, "adjusted"));
        Assert.Equal(3.5m, Num(me, "carried"));
        Assert.Equal(15.5m, Num(me, "available"));
        Assert.Equal(3, me.GetProperty("entries").GetArrayLength());
        Assert.Equal(0m, Num(await Me(k.Emp, k.Admin, 2028), "available"));                        // yıllara göre ayrı
    }

    [Fact]
    public async Task Submitting_reserves_the_days_and_rejection_or_cancellation_gives_them_back()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 14);

        var approved = (await Ask(k, k.Emp))["summary"].GetProperty("id").GetGuid();                // 5 iş günü
        Assert.Equal(9m, Num(await Me(k.Emp, k.Admin), "available"));
        Assert.Equal(5m, Num(await Me(k.Emp, k.Admin), "used"));
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(approved)}/approve");
        Assert.Equal(9m, Num(await Me(k.Emp, k.Admin), "available"));                               // onay bakiyeyi değiştirmez

        var rejected = (await Ask(k, k.Emp, "2027-04-05", "2027-04-07"))["summary"].GetProperty("id").GetGuid();
        Assert.Equal(6m, Num(await Me(k.Emp, k.Admin), "available"));
        await k.Boss.PostAsync($"/api/tasks/{await k.Boss.MyPendingTaskAsync(rejected)}/reject", new { comment = "Yoğun" });
        Assert.Equal(9m, Num(await Me(k.Emp, k.Admin), "available"));

        var cancelled = (await Ask(k, k.Emp, "2027-05-03", "2027-05-04"))["summary"].GetProperty("id").GetGuid();
        Assert.Equal(7m, Num(await Me(k.Emp, k.Admin), "available"));
        await k.Emp.PostAsync($"/api/requests/{cancelled}/cancel");
        Assert.Equal(9m, Num(await Me(k.Emp, k.Admin), "available"));

        var detail = await k.Emp.GetAsync($"/api/requests/{rejected}");
        Assert.Contains("leave_reserved", detail.EventKinds());
        Assert.Contains("leave_refunded", detail.EventKinds());
        var entries = (await Me(k.Emp, k.Admin)).GetProperty("entries").EnumerateArray().ToList();
        Assert.Contains(entries, e => e.GetProperty("kind").GetString() == "usage" && e.TryGetProperty("requestNumber", out var n) && n.GetString()!.StartsWith("IZB-"));
    }

    [Fact]
    public async Task A_request_exceeding_the_available_balance_is_refused_with_the_numbers()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 4);
        var r = await Ask(k, k.Emp);                                                                // 5 gün > 4
        Assert.True(r.Is(HttpStatusCode.UnprocessableEntity), r.ToString());
        Assert.Equal("insufficient_leave_balance", r.Str("code"));
        Assert.Equal(4m, r["details"].GetProperty("available").GetDecimal());
        Assert.Equal(5m, r["details"].GetProperty("requested").GetDecimal());
        Assert.Equal(0, (await k.Emp.GetAsync("/api/my/requests")).Body.GetArrayLength());           // talep oluşmadı
        Assert.Equal(4m, Num(await Me(k.Emp, k.Admin), "available"));

        await Entry(k.Admin, k.Emp, "adjustment", 1);
        Assert.True((await Ask(k, k.Emp)).Is(HttpStatusCode.Created));                              // tam 5 gün: yeterli
        Assert.Equal(0m, Num(await Me(k.Emp, k.Admin), "available"));
        Assert.True((await Ask(k, k.Emp, "2027-06-01", "2027-06-01")).Is(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Only_the_listed_leave_types_use_the_balance()
    {
        var k = await SetupAsync();                                                                  // bakiye 0
        var excused = await Ask(k, k.Emp, kind: "Mazeret izni");
        Assert.True(excused.Is(HttpStatusCode.Created), excused.ToString());
        Assert.Equal(0m, Num(await Me(k.Emp, k.Admin), "used"));
        Assert.DoesNotContain("leave_reserved", excused.EventKinds());
    }

    [Fact]
    public async Task The_balance_of_the_start_years_applies()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 10, 2027);
        Assert.Equal("insufficient_leave_balance", (await Ask(k, k.Emp, "2028-01-03", "2028-01-05")).Str("code"));      // 2028 hakkı yok
        await Entry(k.Admin, k.Emp, "entitlement", 10, 2028);
        Assert.True((await Ask(k, k.Emp, "2028-01-03", "2028-01-05")).Is(HttpStatusCode.Created));
        Assert.Equal(7m, Num(await Me(k.Emp, k.Admin, 2028), "available"));
        Assert.Equal(10m, Num(await Me(k.Emp, k.Admin, 2027), "available"));
    }

    [Fact]
    public async Task Simultaneous_requests_cannot_spend_the_same_days_twice()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 5);
        var results = await Task.WhenAll(Ask(k, k.Emp, "2027-03-01", "2027-03-03", key: "a"), Ask(k, k.Emp, "2027-04-05", "2027-04-07", key: "b"));      // 3 + 3 gün > 5
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.Created)));
        Assert.Equal(1, results.Count(r => r.Is(HttpStatusCode.UnprocessableEntity)));
        Assert.Equal(2m, Num(await Me(k.Emp, k.Admin), "available"));
    }

    [Fact]
    public async Task Apps_without_a_balance_setting_are_unaffected()
    {
        var k = await SetupAsync(balance: false);
        Assert.True((await Ask(k, k.Emp)).Is(HttpStatusCode.Created));
        Assert.Equal(0m, Num(await Me(k.Emp, k.Admin), "used"));
    }

    // ============================================================ yönetim ve toplu işlemler

    [Fact]
    public async Task Entries_are_validated_and_only_for_members_by_admins()
    {
        var k = await SetupAsync();
        var outsider = await Api.RegisterAsync(factory);
        Assert.True((await Entry(k.Emp, k.Emp, "entitlement", 14)).Is(HttpStatusCode.Forbidden));
        Assert.True((await outsider.PostAsync(Ws(k.Admin, "/leave/entries"), new { userId = k.Emp.UserId, year = 2027, kind = "entitlement", days = 14 })).Is(HttpStatusCode.NotFound));
        foreach (var (kind, days, year) in new[] { ("entitlement", -1m, 2027), ("bonus", 1m, 2027), ("adjustment", 400m, 2027), ("adjustment", -400m, 2027), ("entitlement", 14m, 1999), ("entitlement", 14m, 2101), ("usage", 1m, 2027) })
            Assert.True((await Entry(k.Admin, k.Emp, kind, days, year)).Is(HttpStatusCode.UnprocessableEntity), $"{kind} {days} {year}");
        Assert.True((await k.Admin.PostAsync(Ws(k.Admin, "/leave/entries"), new { userId = k.Emp.UserId, year = 2027, kind = "adjustment", days = 1.234 })).Is(HttpStatusCode.UnprocessableEntity));    // en fazla 2 ondalık
        Assert.True((await k.Admin.PostAsync(Ws(k.Admin, "/leave/entries"), new { userId = outsider.UserId, year = 2027, kind = "entitlement", days = 14 })).Is(HttpStatusCode.UnprocessableEntity));    // üye değil
        Assert.True((await Entry(k.Admin, k.Emp, "adjustment", 1, note: new string('x', 201))).Is(HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0m, Num(await Me(k.Emp, k.Admin), "available"));                                // hiçbiri uygulanmadı
    }

    [Fact]
    public async Task The_admin_sees_everyones_balance_and_members_only_their_own()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 14);
        var all = await k.Admin.GetAsync(Ws(k.Admin, "/leave/balances?year=2027"));
        Assert.True(all.Is(HttpStatusCode.OK), all.ToString());
        var row = all.Body.EnumerateArray().First(r => r.GetProperty("userId").GetString() == k.Emp.UserId.ToString());
        Assert.Equal("Çalışan Can", row.GetProperty("name").GetString());
        Assert.Equal(14m, row.GetProperty("available").GetDecimal());
        Assert.True((await k.Emp.GetAsync(Ws(k.Admin, "/leave/balances?year=2027"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await k.Admin.GetAsync(Ws(k.Admin, "/leave/balances?year=1800"))).Is(HttpStatusCode.UnprocessableEntity));
        Assert.True((await k.Emp.GetAsync(Ws(k.Admin, "/leave/me?year=abc"))).Is(HttpStatusCode.UnprocessableEntity));
    }

    [Fact]
    public async Task Distributing_entitlements_uses_hire_dates_and_skips_unknown_or_already_granted()
    {
        var k = await SetupAsync();
        var veteran = await k.Admin.NewUserAsync(factory, displayName: "Kıdemli");
        var rookie = await k.Admin.NewUserAsync(factory, displayName: "Yeni");
        Assert.True((await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{veteran.UserId}"), new { hireDate = "2009-05-04" })).Is(HttpStatusCode.OK));        // 2027'de 18 yıl → 26
        Assert.True((await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"), new { hireDate = "2024-02-01" })).Is(HttpStatusCode.OK));         // 3 yıl → 14
        Assert.True((await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{rookie.UserId}"), new { hireDate = "2027-01-15" })).Is(HttpStatusCode.OK));        // 0 yıl → hak yok
        Assert.True((await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{k.Boss.UserId}"), new { hireDate = "2019-09-09" })).Is(HttpStatusCode.OK));         // 8 yıl → 20

        var run = await k.Admin.PostAsync(Ws(k.Admin, "/leave/distribute"), new { year = 2027 });
        Assert.True(run.Is(HttpStatusCode.OK), run.ToString());
        Assert.Equal(3, run["created"].GetInt32());                                                  // 26 + 14 + 20 (yeni başlayan: hak doğmadı)
        Assert.Equal(1, run["skippedNoEntitlement"].GetInt32());
        Assert.True(run["skippedNoHireDate"].GetInt32() >= 1);                                       // admin'in işe giriş tarihi yok
        Assert.Equal(26m, Num(await Me(veteran, k.Admin), "entitled"));
        Assert.Equal(14m, Num(await Me(k.Emp, k.Admin), "entitled"));
        Assert.Equal(20m, Num(await Me(k.Boss, k.Admin), "entitled"));
        Assert.Equal(0m, Num(await Me(rookie, k.Admin), "entitled"));

        var again = await k.Admin.PostAsync(Ws(k.Admin, "/leave/distribute"), new { year = 2027 });
        Assert.Equal(0, again["created"].GetInt32());
        Assert.Equal(3, again["skippedExisting"].GetInt32());                                        // iki kez hak verilmez
        Assert.Equal(14m, Num(await Me(k.Emp, k.Admin), "entitled"));
    }

    [Fact]
    public async Task Hire_dates_are_validated_and_can_be_cleared()
    {
        var k = await SetupAsync();
        foreach (var bad in new[] { "yarın", "2026-13-01", "2999-01-01", "1899-01-01" })
            Assert.True((await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"), new { hireDate = bad })).Is(HttpStatusCode.UnprocessableEntity), bad);
        var ok = await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"), new { hireDate = "2020-01-01" });
        Assert.Equal("2020-01-01", ok.Str("hireDate"));
        var cleared = await k.Admin.PatchAsync(Ws(k.Admin, $"/members/{k.Emp.UserId}"), new { clearHireDate = true });
        Assert.False(cleared.Body.TryGetProperty("hireDate", out _));
    }

    [Fact]
    public async Task Carryover_moves_the_remaining_days_once_and_respects_the_cap()
    {
        var k = await SetupAsync();
        await Entry(k.Admin, k.Emp, "entitlement", 14, 2026);
        await Ask(k, k.Emp, "2026-03-02", "2026-03-04");                                             // 3 gün kullanıldı → 11 kaldı
        Assert.Equal(11m, Num(await Me(k.Emp, k.Admin, 2026), "available"));

        var run = await k.Admin.PostAsync(Ws(k.Admin, "/leave/carryover"), new { fromYear = 2026, maxDays = 5 });
        Assert.True(run.Is(HttpStatusCode.OK), run.ToString());
        Assert.Equal(1, run["created"].GetInt32());
        Assert.Equal(5m, run["totalDays"].GetDecimal());
        Assert.Equal(5m, Num(await Me(k.Emp, k.Admin, 2027), "carried"));
        Assert.Equal(0, (await k.Admin.PostAsync(Ws(k.Admin, "/leave/carryover"), new { fromYear = 2026 }))["created"].GetInt32());     // ikinci kez devredilmez
        Assert.True((await k.Admin.PostAsync(Ws(k.Admin, "/leave/carryover"), new { fromYear = 2026, maxDays = -1 })).Is(HttpStatusCode.UnprocessableEntity));
        Assert.True((await k.Emp.PostAsync(Ws(k.Admin, "/leave/carryover"), new { fromYear = 2026 })).Is(HttpStatusCode.Forbidden));
    }

    // ================================================================== tasarım

    [Fact]
    public async Task A_broken_balance_definition_cannot_be_published()
    {
        var admin = await Api.RegisterAsync(factory);
        var def = new
        {
            schema = 1, prefix = "BAD", summaryFields = Array.Empty<string>(),
            fields = new object[] { new { key = "start", label = "Başlangıç", type = "date", required = true }, new { key = "days", label = "Gün", type = "number" } },
            steps = new object[] { new { id = "s1", name = "Y", approvers = new { type = "admin" } } },
            completion = new { leave = new { enabled = true, daysField = "start", startField = "days" } },        // alan türleri ters
        };
        var created = await admin.PostAsync(Ws(admin, "/apps"), new { name = "Bozuk", definition = def });
        Assert.Contains(created["issues"].EnumerateArray(), i => i.GetProperty("code").GetString() == "invalid_leave");
        Assert.True((await admin.PostAsync($"/api/apps/{created["summary"].GetProperty("id").GetString()}/publish")).Is(HttpStatusCode.UnprocessableEntity));
    }
}
