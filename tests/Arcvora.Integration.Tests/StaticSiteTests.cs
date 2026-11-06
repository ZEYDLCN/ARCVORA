using System.Net;
using Arcvora.Api;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Arcvora.Integration.Tests;

public class StaticSitePolicyTests
{
    [Theory]
    [InlineData("/assets/index-Ab12Cd.js")]
    [InlineData("/assets/index-Ab12Cd.css")]
    [InlineData("/assets/logo-9f8e7d.svg")]
    public void Ozetli_varliklar_bir_yil_degismez_onbelleklenir(string path) =>
        Assert.Equal("public, max-age=31536000, immutable", StaticSite.CacheControl(path));

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/sw.js")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/requests/3f2c")]            // SPA yolu: kabuk (index.html) döner, güncel olmalı
    [InlineData("/ASSETS/x.js")]              // büyük/küçük harf farkıyla "assets" sayılmaz: güvenli tarafta kal
    public void Kabuk_service_worker_ve_manifest_her_seferinde_dogrulanir(string path) =>
        Assert.Equal("no-cache", StaticSite.CacheControl(path));

    [Fact]
    public void Simgeler_bir_gun_onbelleklenir() =>
        Assert.Equal("public, max-age=86400", StaticSite.CacheControl("/icons/icon-192.png"));
}

/// <summary>Derlenmiş arayüzü geçici bir wwwroot'tan sunan gerçek bir sunucuya karşı: başlıklar, SPA düşüşü, /api ayrımı.</summary>
public class StaticSiteHostingTests : IClassFixture<StaticSiteFactory>
{
    private readonly HttpClient _c;
    public StaticSiteHostingTests(StaticSiteFactory f) => _c = f.CreateClient();

    [Fact]
    public async Task Kok_index_html_doner_ve_dogrulanir()
    {
        var r = await _c.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("ARCVORA-SHELL", await r.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task SPA_yollari_kabuga_duser_ve_bayat_onbellek_birakmaz()
    {
        var r = await _c.GetAsync("/requests/123");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("ARCVORA-SHELL", await r.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Service_worker_javascript_olarak_ve_onbelleksiz_sunulur()
    {
        var r = await _c.GetAsync("/sw.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("javascript", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Manifest_dogru_icerik_turuyle_sunulur()
    {
        var r = await _c.GetAsync("/manifest.webmanifest");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/manifest+json", r.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ozetli_varlik_degismez_onbellek_basligi_alir()
    {
        var r = await _c.GetAsync("/assets/index-abc123.js");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Eksik_varlik_kabuga_dusmez_404_doner()
    {
        var r = await _c.GetAsync("/assets/eski-yok.js");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Bilinmeyen_api_yolu_kabuga_dusmez_json_404_doner()
    {
        var r = await _c.GetAsync("/api/yok-boyle-bir-sey");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("ARCVORA-SHELL", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Api_yanitlari_onbelleklenmez_basligi_tasimaz_degismez_degildir()
    {
        var r = await _c.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.NotEqual("public, max-age=31536000, immutable", r.Headers.CacheControl?.ToString());
    }
}

public class StaticSiteFactory : ApiFactory
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "arcvora_wwwroot_" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var www = Path.Combine(_root, "wwwroot");
        Directory.CreateDirectory(Path.Combine(www, "assets"));
        Directory.CreateDirectory(Path.Combine(www, "icons"));
        File.WriteAllText(Path.Combine(www, "index.html"), "<!doctype html><title>ARCVORA-SHELL</title>");
        File.WriteAllText(Path.Combine(www, "sw.js"), "self.addEventListener('fetch',()=>{});");
        File.WriteAllText(Path.Combine(www, "manifest.webmanifest"), "{\"name\":\"ARCVORA Apps\"}");
        File.WriteAllText(Path.Combine(www, "assets", "index-abc123.js"), "console.log(1)");
        base.ConfigureWebHost(builder);
        builder.UseContentRoot(_root);
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(_root, true); } catch (IOException) { /* geçici klasör; kalırsa sistem temizler */ }
    }
}
