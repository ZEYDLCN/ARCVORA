using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Arcvora.Integration.Tests;

/// <summary>Herkese açık (ücretsiz) bir dağıtımda kayıt kapatılabilir: yabancılar hesap/proje açamaz, mevcut kullanıcılar girebilir.</summary>
public class RegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(HttpClient c, string url, object body)
    {
        using var res = await c.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    [Fact]
    public async Task Registration_is_open_by_default()
    {
        var r = await PostAsync(factory.CreateClient(), "/api/auth/register", new { email = $"open-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Created, r.Status);
    }

    [Fact]
    public async Task Closed_registration_returns_403_but_existing_users_can_still_log_in()
    {
        var existing = await Api.RegisterAsync(factory, $"keep-{Guid.NewGuid():N}@example.com");
        using var closed = factory.WithWebHostBuilder(b => b.UseSetting("Auth:RegistrationOpen", "false"));
        var client = closed.CreateClient();

        var blocked = await PostAsync(client, "/api/auth/register", new { email = $"new-{Guid.NewGuid():N}@example.com", password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Forbidden, blocked.Status);
        Assert.Equal("registration_closed", blocked.Body.GetProperty("code").GetString());
        Assert.Contains("kayıt", blocked.Body.GetProperty("title").GetString(), StringComparison.OrdinalIgnoreCase);

        var login = await PostAsync(client, "/api/auth/login", new { email = existing.Email, password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, login.Status);
    }

    [Fact]
    public async Task Closed_registration_does_not_create_a_user_row()
    {
        using var closed = factory.WithWebHostBuilder(b => b.UseSetting("Auth:RegistrationOpen", "false"));
        var email = $"ghost-{Guid.NewGuid():N}@example.com";
        await PostAsync(closed.CreateClient(), "/api/auth/register", new { email, password = "correct-horse-battery" });
        var login = await PostAsync(factory.CreateClient(), "/api/auth/login", new { email, password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.Status);
    }
}
