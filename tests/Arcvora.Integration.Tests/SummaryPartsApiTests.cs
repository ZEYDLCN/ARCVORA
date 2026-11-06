using System.Net;
using System.Text.Json;

namespace Arcvora.Integration.Tests;

/// <summary>Liste kartları için yapılandırılmış özet (etiketli tarih/tutar kutuları) ve demo işareti: talep, görev ve arama listelerinde aynı biçim.</summary>
public class SummaryPartsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Kit(Api Admin, Api Boss, Api Emp, Guid Leave, Guid Request);

    private async Task<Kit> SetupAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Müdür Mehmet");
        var emp = await admin.NewUserAsync(factory, displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin Talebi", emp.UserId);
        var r = await emp.SubmitAsync(app, AppsKit.Leave());
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return new Kit(admin, boss, emp, app, r["summary"].GetProperty("id").GetGuid());
    }

    private static string[] Labels(JsonElement parts) => parts.EnumerateArray().Select(p => p.GetProperty("label").GetString()!).ToArray();
    private static string[] Kinds(JsonElement parts) => parts.EnumerateArray().Select(p => p.GetProperty("kind").GetString()!).ToArray();
    private static string[] Texts(JsonElement parts) => parts.EnumerateArray().Select(p => p.GetProperty("text").GetString()!).ToArray();

    [Fact]
    public async Task My_requests_carry_labelled_summary_parts_beside_the_plain_summary()
    {
        var k = await SetupAsync();
        var list = await k.Emp.GetAsync("/api/my/requests");
        var item = list.Body.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == k.Request);
        var parts = item.GetProperty("summaryParts");
        Assert.Equal(["Başlangıç", "Bitiş", "Tür"], Labels(parts));
        Assert.Equal(["date", "date", "text"], Kinds(parts));
        Assert.Equal(["10.11.2026", "14.11.2026", "Yıllık"], Texts(parts));
        Assert.Equal("10.11.2026 · 14.11.2026 · Yıllık", item.GetProperty("summary").GetString());      // eski alan aynen durur
        Assert.False(item.TryGetProperty("isDemo", out var demo) && demo.GetBoolean());                     // demo değil: alan yok ya da false
    }

    [Fact]
    public async Task Tasks_in_the_inbox_carry_the_same_parts()
    {
        var k = await SetupAsync();
        var tasks = await k.Boss.GetAsync("/api/my/tasks");
        var t = tasks.Body.EnumerateArray().Single(x => x.GetProperty("requestId").GetGuid() == k.Request);
        Assert.Equal(["10.11.2026", "14.11.2026", "Yıllık"], Texts(t.GetProperty("summaryParts")));
        Assert.Equal(["Başlangıç", "Bitiş", "Tür"], Labels(t.GetProperty("summaryParts")));
    }

    [Fact]
    public async Task Admin_search_results_carry_the_parts_too()
    {
        var k = await SetupAsync();
        var r = await k.Admin.GetAsync($"/api/workspaces/{k.Admin.WorkspaceId}/requests/search");
        var item = r["items"].EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == k.Request);
        Assert.Equal(3, item.GetProperty("summaryParts").GetArrayLength());
    }

    [Fact]
    public async Task Requests_keep_the_labels_of_the_version_they_were_opened_with()
    {
        var k = await SetupAsync();
        // Uygulama yeniden adlandırılan etiketle yeni sürüm yayınlar; eski talep eski sürümün etiketlerini göstermeye devam eder.
        var json = JsonSerializer.Serialize(AppsKit.LeaveDef(), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).Replace("\"Başlangıç\"", "\"İzin başlangıcı\"");
        Assert.Contains("İzin başlangıcı", json);
        var current = await k.Admin.GetAsync($"/api/apps/{k.Leave}");
        var draft = await k.Admin.PutAsync($"/api/apps/{k.Leave}", new { baseRevision = current["summary"].GetProperty("revision").GetInt32(), definition = JsonDocument.Parse(json).RootElement });
        Assert.True(draft.Is(HttpStatusCode.OK), draft.ToString());
        Assert.True((await k.Admin.PostAsync($"/api/apps/{k.Leave}/publish")).Is(HttpStatusCode.OK));

        var item = (await k.Emp.GetAsync("/api/my/requests")).Body.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == k.Request);
        Assert.Equal("Başlangıç", Labels(item.GetProperty("summaryParts"))[0]);
    }
}
