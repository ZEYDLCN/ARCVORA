using System.Net;

namespace Arcvora.Integration.Tests;

/// <summary>QA: KVKK dışa aktarımı kişinin tüm verisini kapsamalı — izin hakları/düşümleri dahil.</summary>
public class QaPersonalDataTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object BalanceLeave() => new
    {
        schema = 1, prefix = "IZB", summaryFields = new[] { "start", "end" },
        fields = new object[]
        {
            new { key = "start", label = "Başlangıç", type = "date", required = true },
            new { key = "end", label = "Bitiş", type = "date", required = true, notBefore = "start" },
            new { key = "days", label = "Gün", type = "number", computed = new { op = "calendar_days", from = "start", to = "end" } },
        },
        steps = new object[] { new { id = "s1", name = "Yönetici onayı", approvers = new { type = "manager" } } },
        completion = new { leave = new { enabled = true, daysField = "days", startField = "start" } },
    };

    [Fact]
    public async Task The_export_lists_leave_entitlements_and_deductions_with_their_request_numbers()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(BalanceLeave(), "İzin", emp.UserId);
        var ws = $"/api/workspaces/{admin.WorkspaceId}";

        var grant = await admin.PostAsync($"{ws}/leave/entries", new { userId = emp.UserId, year = 2027, kind = "entitlement", days = 14, note = "Kıdem hakkı" });
        Assert.True(grant.Is(HttpStatusCode.OK) || grant.Is(HttpStatusCode.Created), grant.ToString());
        var sub = await emp.SubmitAsync(app, new { start = "2027-03-01", end = "2027-03-03" });
        Assert.True(sub.Is(HttpStatusCode.Created), sub.ToString());
        var number = sub["summary"].GetProperty("number").GetString();

        var export = await emp.GetAsync($"{ws}/members/{emp.UserId}/data-export");
        Assert.True(export.Is(HttpStatusCode.OK), export.ToString());
        var leave = export["leave"];
        Assert.True(leave.GetArrayLength() >= 2, leave.GetRawText());          // hak + rezerve edilen gün
        var rows = leave.EnumerateArray().ToList();
        Assert.Contains(rows, r => r.GetProperty("kind").GetString() == "entitlement" && r.GetProperty("days").GetDecimal() == 14m && r.GetProperty("note").GetString() == "Kıdem hakkı");
        Assert.Contains(rows, r => r.GetProperty("requestNumber").ValueKind == System.Text.Json.JsonValueKind.String && r.GetProperty("requestNumber").GetString() == number && r.GetProperty("days").GetDecimal() < 0);
        Assert.All(rows, r => Assert.Equal(2027, r.GetProperty("year").GetInt32()));
    }

    [Fact]
    public async Task Someone_elses_leave_never_appears_in_my_export()
    {
        var admin = await Api.RegisterAsync(factory);
        var a = await admin.NewUserAsync(factory, displayName: "A");
        var b = await admin.NewUserAsync(factory, displayName: "B");
        var ws = $"/api/workspaces/{admin.WorkspaceId}";
        await admin.PostAsync($"{ws}/leave/entries", new { userId = a.UserId, year = 2027, kind = "entitlement", days = 20, note = "A'nın hakkı" });
        await admin.PostAsync($"{ws}/leave/entries", new { userId = b.UserId, year = 2027, kind = "entitlement", days = 26, note = "B'nin hakkı" });

        var export = await b.GetAsync($"{ws}/members/{b.UserId}/data-export");
        Assert.Equal(1, export["leave"].GetArrayLength());
        Assert.DoesNotContain("A'nın", export.Body.GetRawText());
    }

    [Fact]
    public async Task A_person_without_leave_has_an_empty_list_not_a_missing_field()
    {
        var admin = await Api.RegisterAsync(factory);
        var p = await admin.NewUserAsync(factory, displayName: "Hiç izni yok");
        var export = await p.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members/{p.UserId}/data-export");
        Assert.Equal(0, export["leave"].GetArrayLength());
    }
}
