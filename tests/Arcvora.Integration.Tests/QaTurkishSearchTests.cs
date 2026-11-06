using System.Net;

namespace Arcvora.Integration.Tests;

/// <summary>
/// QA: Türkçe arama. Kullanıcılar çoğu kez Türkçe harfsiz yazar ("sirket", "isil") ya da büyük/küçük harfi karıştırır ("ISIL", "işıl");
/// PostgreSQL ILIKE ı/İ/ş/ğ/ü/ö/ç'yi eşlemez. Arama bunları katlayarak yapılmalı.
/// </summary>
public class QaTurkishSearchTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(Api Admin, string Number)> SetupAsync()
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: "Müdür Mehmet");
        var isil = await admin.NewUserAsync(factory, "app_user", displayName: "Işıl Öztürk", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.ExpenseDef(), "Masraf", isil.UserId);
        var r = await isil.SubmitAsync(app, new { amount = 100m, description = "Şirket gideri: İstanbul ziyareti, Işık Gıda" });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        return (admin, r["summary"].GetProperty("number").GetString()!);
    }

    private static async Task<int> Found(Api a, string q) =>
        (await a.GetAsync($"/api/workspaces/{a.WorkspaceId}/requests/search?q={Uri.EscapeDataString(q)}"))["total"].GetInt32();

    [Theory]
    // kişi adı: "Işıl Öztürk"
    [InlineData("Işıl")] [InlineData("ışıl")] [InlineData("ISIL")] [InlineData("isil")] [InlineData("İŞİL")]
    [InlineData("öztürk")] [InlineData("ozturk")] [InlineData("OZTURK")] [InlineData("ÖZTÜRK")]
    // özet: "Şirket gideri: İstanbul ziyareti, Işık Gıda"
    [InlineData("şirket")] [InlineData("sirket")] [InlineData("ŞİRKET")] [InlineData("SIRKET")]
    [InlineData("istanbul")] [InlineData("İSTANBUL")] [InlineData("Istanbul")]
    [InlineData("ışık gıda")] [InlineData("isik gida")] [InlineData("IŞIK GIDA")]
    public async Task Turkish_letters_match_with_or_without_diacritics_and_in_any_case(string query)
    {
        var (admin, _) = await SetupAsync();
        Assert.Equal(1, await Found(admin, query));
    }

    [Theory]
    [InlineData("xyz")] [InlineData("sirketi")] [InlineData("Muhasebe")]
    public async Task Unrelated_terms_still_find_nothing(string query)
    {
        var (admin, _) = await SetupAsync();
        Assert.Equal(0, await Found(admin, query));
    }

    [Fact]
    public async Task Wildcards_stay_literal_after_folding()
    {
        var (admin, _) = await SetupAsync();
        Assert.Equal(0, await Found(admin, "%"));
        Assert.Equal(0, await Found(admin, "_"));
        Assert.Equal(0, await Found(admin, "ş%"));
    }

    [Fact]
    public async Task The_request_number_is_still_searchable_in_any_case()
    {
        var (admin, number) = await SetupAsync();
        Assert.Equal(1, await Found(admin, number.ToLowerInvariant()));
        Assert.Equal(1, await Found(admin, number[..6]));
    }
}
