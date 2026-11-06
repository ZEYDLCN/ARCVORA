using System.Net;
using Arcvora.Infrastructure.Common;
using Arcvora.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Arcvora.Integration.Tests;

public class SsoApiFactory : ApiFactory
{
    public MockIdp Idp { get; } = new();
    protected virtual bool AutoProvision => false;
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["Oidc:Enabled"] = "true", ["Oidc:Authority"] = Idp.Issuer, ["Oidc:ClientId"] = Idp.ClientId, ["Oidc:ClientSecret"] = Idp.ClientSecret,
        ["Oidc:PublicUrl"] = "https://apps.test", ["Oidc:AllowInsecureHttp"] = "true",
        ["Oidc:AutoProvision"] = AutoProvision ? "true" : "false", ["Oidc:DefaultWorkspaceId"] = AutoProvision ? Guid.NewGuid().ToString() : "",
    };

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await Idp.DisposeAsync();
    }
}

public sealed class SsoProvisionFactory : SsoApiFactory { protected override bool AutoProvision => true; }

/// <summary>G8: tek oturum açma (OIDC) — sahte kimlik sağlayıcıyla uçtan uca: PKCE, imza, nonce, kitle, veren, süre, kimlik bağlama, bilet.</summary>
public class SsoTests(SsoApiFactory factory) : IClassFixture<SsoApiFactory>
{
    private MockIdp Idp => factory.Idp;
    private OidcOptions Options => factory.Services.GetRequiredService<IOptions<OidcOptions>>().Value;

    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static string Param(string location, string name)
    {
        var frag = location[(location.IndexOf("#/sso?", StringComparison.Ordinal) + 6)..];
        return System.Web.HttpUtility.ParseQueryString(frag)[name] ?? "";
    }

    private async Task<string> Login(string sub, string? email, Action<MockIdp.Tamper>? tamper = null, string? name = "SSO Kişi", bool? verified = true, HttpClient? client = null, string? stateOverride = null)
    {
        var c = client ?? NewClient();
        var start = await c.GetAsync("/api/auth/oidc/login");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var tamperObj = new MockIdp.Tamper();
        tamper?.Invoke(tamperObj);
        Idp.Next = tamperObj;
        var (code, state) = Idp.Authorize(start.Headers.Location!, sub, email, name, verified);
        var cb = await c.GetAsync($"/api/auth/oidc/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(stateOverride ?? state)}");
        Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
        Idp.Next = new MockIdp.Tamper();
        return cb.Headers.Location!.ToString();
    }

    private async Task<Resp> Ticket(string? ticket) => await new Api(factory.CreateClient()).PostAsync("/api/auth/oidc/ticket", new { ticket });

    private async Task<(Api Admin, Api User)> UserAsync(string? email = null)
    {
        var admin = await Api.RegisterAsync(factory);
        var u = await admin.NewUserAsync(factory);
        return (admin, u);
    }

    private async Task<Resp> SignedIn(string location)
    {
        var ticket = Param(location, "ticket");
        Assert.NotEmpty(ticket);
        var r = await Ticket(ticket);
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
        return r;
    }

    // ============================================================== yönlendirme

    [Fact]
    public async Task The_login_redirect_carries_pkce_state_nonce_and_a_locked_down_state_cookie()
    {
        using var c = NewClient();
        var res = await c.GetAsync("/api/auth/oidc/login");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        var loc = res.Headers.Location!;
        Assert.StartsWith(Idp.Issuer + "/authorize", loc.ToString());
        var q = System.Web.HttpUtility.ParseQueryString(loc.Query);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal(Idp.ClientId, q["client_id"]);
        Assert.Equal("https://apps.test/api/auth/oidc/callback", q["redirect_uri"]);
        Assert.Equal("openid profile email", q["scope"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.True(q["state"]!.Length >= 24 && q["nonce"]!.Length >= 24 && q["code_challenge"]!.Length >= 40);
        Assert.NotEqual(q["state"], q["nonce"]);

        var cookie = res.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("arcvora_oidc="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth/oidc", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(q["nonce"]!, cookie.Split(';')[0].Split('.')[1]);                                  // imza bölümünde düz değer yok
        // İki ayrı giriş farklı değerler üretir.
        var again = await c.GetAsync("/api/auth/oidc/login");
        Assert.NotEqual(q["state"], System.Web.HttpUtility.ParseQueryString(again.Headers.Location!.Query)["state"]);
    }

    [Fact]
    public async Task Providers_reports_the_button_and_password_login_keeps_working()
    {
        var p = await new Api(factory.CreateClient()).GetAsync("/api/auth/providers");
        Assert.True(p["oidc"].GetBoolean());
        Assert.Equal("Şirket hesabıyla giriş", p.Str("oidcLabel"));
        var admin = await Api.RegisterAsync(factory, password: "correct-horse-battery");
        Assert.True((await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email = admin.Email, password = "correct-horse-battery" })).Is(HttpStatusCode.OK));
    }

    // ============================================================== başarılı giriş

    [Fact]
    public async Task An_existing_account_signs_in_by_email_and_gets_a_normal_session()
    {
        var (admin, user) = await UserAsync();
        var loc = await Login("sub-" + Guid.NewGuid().ToString("N"), user.Email.ToUpperInvariant());                // e-posta büyük/küçük harf duyarsız
        Assert.StartsWith("https://apps.test/#/sso?ticket=", loc);
        var session = await SignedIn(loc);
        Assert.Equal(user.Email, session["user"].GetProperty("email").GetString());
        Assert.False(session["mustChangePassword"].GetBoolean());
        Assert.Equal(1, session["workspaces"].GetArrayLength());

        var me = new HttpClient();
        using var api = factory.CreateClient();
        api.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.Str("token"));
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/me")).StatusCode);
        Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/audit?action=sso.linked"))["total"].GetInt32());
        me.Dispose();
    }

    [Fact]
    public async Task The_identity_is_linked_so_a_changed_email_still_finds_the_same_person_but_a_new_subject_cannot_take_the_account()
    {
        var (_, user) = await UserAsync();
        var sub = "sub-" + Guid.NewGuid().ToString("N");
        var first = await SignedIn(await Login(sub, user.Email));
        var second = await SignedIn(await Login(sub, "tamamen-baska@firma.com"));                         // aynı konu, yeni e-posta → aynı kişi
        Assert.Equal(first["user"].GetProperty("id").GetString(), second["user"].GetProperty("id").GetString());

        var hijack = await Login("sub-" + Guid.NewGuid().ToString("N"), user.Email);                      // aynı e-posta, başka konu
        Assert.Equal("sso_conflict", Param(hijack, "error"));
    }

    // ============================================================== reddedilenler

    [Fact]
    public async Task Unknown_emails_are_refused_when_auto_provisioning_is_off_and_nothing_is_created()
    {
        var email = $"yabanci-{Guid.NewGuid():N}@firma.com";
        Assert.Equal("sso_not_allowed", Param(await Login("s1", email), "error"));
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>().Users.AnyAsync(u => u.Email == email));
    }

    [Theory]
    [InlineData(false, false, "sso_email_unverified")]        // doğrulanmamış
    [InlineData(null, false, "sso_email_unverified")]         // talep yok (RequireEmailVerified açık)
    public async Task Unverified_emails_are_refused(bool? verified, bool _, string expected)
    {
        var (_, user) = await UserAsync();
        Assert.Equal(expected, Param(await Login("s-" + Guid.NewGuid().ToString("N"), user.Email, verified: verified), "error"));
    }

    [Fact]
    public async Task A_verified_flag_sent_as_a_string_is_accepted_and_a_missing_email_is_refused()
    {
        var (_, user) = await UserAsync();
        Assert.StartsWith("https://apps.test/#/sso?ticket=", await Login("s-" + Guid.NewGuid().ToString("N"), user.Email, t => t.EmailVerifiedAsString = true));
        Assert.Equal("sso_no_email", Param(await Login("s2", null), "error"));
        Assert.Equal("sso_no_email", Param(await Login("s3", "bu bir eposta degil"), "error"));
    }

    [Fact]
    public async Task The_email_requirement_can_be_relaxed_for_tenant_bound_providers()
    {
        var (_, user) = await UserAsync();
        Options.RequireEmailVerified = false;
        try { Assert.StartsWith("https://apps.test/#/sso?ticket=", await Login("s-" + Guid.NewGuid().ToString("N"), user.Email, verified: null)); }
        finally { Options.RequireEmailVerified = true; }
        Assert.Equal("sso_email_unverified", Param(await Login("s9", user.Email, verified: false), "error"));      // açıkça yanlış hep reddedilir
    }

    [Fact]
    public async Task Tokens_with_a_wrong_nonce_audience_issuer_expiry_or_signature_are_refused()
    {
        var (_, user) = await UserAsync();
        foreach (var (name, tamper) in new (string, Action<MockIdp.Tamper>)[]
        {
            ("nonce", t => t.Nonce = "baska-bir-nonce"), ("nonce yok", t => t.OmitNonce = true), ("kitle", t => t.Audience = "baska-uygulama"), ("veren", t => t.Issuer = "https://kotu.example"),
            ("süre", t => t.ExpiresInSeconds = -600), ("imza", t => t.SignWithOtherKey = true),
        })
        {
            var loc = await Login("s-" + Guid.NewGuid().ToString("N"), user.Email, tamper);
            Assert.True(Param(loc, "error") == "sso_failed", $"{name}: {loc}");
            Assert.DoesNotContain("ticket=", loc);
        }
        Assert.StartsWith("https://apps.test/#/sso?ticket=", await Login("s-" + Guid.NewGuid().ToString("N"), user.Email));       // sağlam akış hâlâ çalışır
    }

    [Fact]
    public async Task State_and_cookie_problems_are_refused()
    {
        var (_, user) = await UserAsync();
        Assert.Equal("sso_failed", Param(await Login("s1", user.Email, stateOverride: "uydurma-state"), "error"));

        // Çerezsiz istemci (başka tarayıcıda açılan bağlantı): durum yok.
        using var c1 = NewClient();
        var start = await c1.GetAsync("/api/auth/oidc/login");
        var (code, state) = Idp.Authorize(start.Headers.Location!, "s2", user.Email);
        using var c2 = NewClient();
        var loc = (await c2.GetAsync($"/api/auth/oidc/callback?code={code}&state={state}")).Headers.Location!.ToString();
        Assert.Equal("sso_expired", Param(loc, "error"));

        // Aynı geri dönüş ikinci kez oynatılamaz (çerez silinir, kod da tek kullanımlıktır).
        var ok = await c1.GetAsync($"/api/auth/oidc/callback?code={code}&state={state}");
        Assert.Contains("ticket=", ok.Headers.Location!.ToString());
        var replay = await c1.GetAsync($"/api/auth/oidc/callback?code={code}&state={state}");
        Assert.Equal("sso_expired", Param(replay.Headers.Location!.ToString(), "error"));

        // Bozulmuş çerez.
        using var c3 = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = factory.Server.BaseAddress };
        _ = c3;
        var bad = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/oidc/callback?code=x&state=y");
        req.Headers.Add("Cookie", "arcvora_oidc=bozuk.deger");
        Assert.Equal("sso_expired", Param((await bad.SendAsync(req)).Headers.Location!.ToString(), "error"));
    }

    [Fact]
    public async Task Provider_failures_never_leak_details_to_the_browser()
    {
        var (_, user) = await UserAsync();
        foreach (var tamper in new Action<MockIdp.Tamper>[] { t => t.TokenStatus = 500, t => t.OmitIdToken = true, t => t.ClientSecret = "yanlis" })
        {
            var loc = await Login("s-" + Guid.NewGuid().ToString("N"), user.Email, tamper);
            Assert.Equal("sso_failed", Param(loc, "error"));
            Assert.DoesNotContain("Exception", loc);
            Assert.DoesNotContain(Idp.Issuer, loc);
            Assert.DoesNotContain(Idp.ClientSecret, loc);
        }
        using var c = NewClient();
        await c.GetAsync("/api/auth/oidc/login");
        var denied = await c.GetAsync("/api/auth/oidc/callback?error=access_denied&state=x");
        Assert.Equal("sso_denied", Param(denied.Headers.Location!.ToString(), "error"));
    }

    [Fact]
    public async Task Only_allowed_email_domains_may_sign_in_when_a_list_is_configured()
    {
        var (_, user) = await UserAsync();
        Options.AllowedEmailDomains = ["baska-firma.com"];
        try { Assert.Equal("sso_not_allowed", Param(await Login("s-" + Guid.NewGuid().ToString("N"), user.Email), "error")); }
        finally { Options.AllowedEmailDomains = []; }
        Assert.Contains("ticket=", await Login("s-" + Guid.NewGuid().ToString("N"), user.Email));
    }

    [Fact]
    public async Task Anonymised_accounts_cannot_sign_in_again()
    {
        var (admin, user) = await UserAsync();
        var sub = "sub-" + Guid.NewGuid().ToString("N");
        await SignedIn(await Login(sub, user.Email));
        await admin.DeleteAsync($"/api/workspaces/{admin.WorkspaceId}/members/{user.UserId}");
        Assert.True((await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/members/{user.UserId}/anonymize")).Is(HttpStatusCode.OK));
        Assert.Equal("sso_not_allowed", Param(await Login(sub, user.Email), "error"));
        var viaTicket = await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email = user.Email, password = "temporary-pass-1" });
        Assert.True(viaTicket.Is(HttpStatusCode.Unauthorized));
    }

    // ================================================================= bilet

    [Fact]
    public async Task A_ticket_works_once_and_forged_or_expired_tickets_do_not()
    {
        var (_, user) = await UserAsync();
        var sub = "sub-" + Guid.NewGuid().ToString("N");                                  // aynı kişi: aynı konu
        var ticket = Param(await Login(sub, user.Email), "ticket");
        Assert.True((await Ticket(ticket)).Is(HttpStatusCode.OK));
        var second = await Ticket(ticket);
        Assert.True(second.Is(HttpStatusCode.Unauthorized), second.ToString());
        Assert.Equal("invalid_ticket", second.Str("code"));

        var fresh = Param(await Login(sub, user.Email), "ticket");
        var forged = fresh[..^3] + (fresh[^1] == 'A' ? "BBB" : "AAA");
        Assert.True((await Ticket(forged)).Is(HttpStatusCode.Unauthorized));
        foreach (var junk in new[] { "", "abc", "a.b", "....", null }) Assert.True((await Ticket(junk)).Is(HttpStatusCode.Unauthorized), junk ?? "null");

        Options.TicketSeconds = -10;
        string expired;
        try { expired = Param(await Login(sub, user.Email), "ticket"); }
        finally { Options.TicketSeconds = 60; }
        Assert.True((await Ticket(expired)).Is(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task A_ticket_cannot_be_replayed_in_parallel()
    {
        var (_, user) = await UserAsync();
        var ticket = Param(await Login("s-" + Guid.NewGuid().ToString("N"), user.Email), "ticket");
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Ticket(ticket)));
        Assert.True(results.Count(r => r.Is(HttpStatusCode.OK)) <= 1, string.Join(",", results.Select(r => (int)r.Status)));
    }
}

public class SsoProvisioningTests(SsoProvisionFactory factory) : IClassFixture<SsoProvisionFactory>
{
    private MockIdp Idp => factory.Idp;
    private HttpClient NewClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task<string> Login(string sub, string email, string? name = "Yeni Kişi")
    {
        using var c = NewClient();
        var start = await c.GetAsync("/api/auth/oidc/login");
        var (code, state) = Idp.Authorize(start.Headers.Location!, sub, email, name);
        return (await c.GetAsync($"/api/auth/oidc/callback?code={code}&state={state}")).Headers.Location!.ToString();
    }

    private static string Ticket(string loc) => System.Web.HttpUtility.ParseQueryString(loc[(loc.IndexOf("#/sso?", StringComparison.Ordinal) + 6)..])["ticket"] ?? "";
    private static string Error(string loc) => System.Web.HttpUtility.ParseQueryString(loc[(loc.IndexOf("#/sso?", StringComparison.Ordinal) + 6)..])["error"] ?? "";

    [Fact]
    public async Task New_people_get_an_app_user_account_in_the_default_workspace_without_a_usable_password()
    {
        var admin = await Api.RegisterAsync(factory);
        factory.Services.GetRequiredService<IOptions<OidcOptions>>().Value.DefaultWorkspaceId = admin.WorkspaceId.ToString();
        var email = $"yeni-{Guid.NewGuid():N}@firma.com";
        var loc = await Login("sub-new-1", email.ToUpperInvariant(), "  Ayşe Şahin  ");
        var session = await new Api(factory.CreateClient()).PostAsync("/api/auth/oidc/ticket", new { ticket = Ticket(loc) });
        Assert.True(session.Is(HttpStatusCode.OK), session.ToString());
        Assert.Equal(email, session["user"].GetProperty("email").GetString());
        Assert.Equal("Ayşe Şahin", session["user"].GetProperty("displayName").GetString());
        Assert.Equal("app_user", session["workspaces"][0].GetProperty("role").GetString());
        Assert.Equal(admin.WorkspaceId.ToString(), session["workspaces"][0].GetProperty("id").GetString());

        foreach (var pw in new[] { "", "password", "disabled$", "correct-horse-battery" })
            Assert.True((await new Api(factory.CreateClient()).PostAsync("/api/auth/login", new { email, password = pw })).Is(HttpStatusCode.Unauthorized), pw);

        // İkinci giriş yeni hesap açmaz.
        await Login("sub-new-1", email);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>().Users.CountAsync(u => u.Email == email));
        var members = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/members");
        Assert.Contains(members.Body.EnumerateArray(), m => m.GetProperty("email").GetString() == email && m.GetProperty("role").GetString() == "app_user");
        Assert.Equal(1, (await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/audit?action=sso.provisioned"))["total"].GetInt32());
    }

    [Fact]
    public async Task Simultaneous_first_sign_ins_create_exactly_one_account()
    {
        var admin = await Api.RegisterAsync(factory);
        factory.Services.GetRequiredService<IOptions<OidcOptions>>().Value.DefaultWorkspaceId = admin.WorkspaceId.ToString();
        var email = $"es-{Guid.NewGuid():N}@firma.com";
        var results = await Task.WhenAll(Login("sub-race", email), Login("sub-race", email), Login("sub-race", email));
        Assert.True(results.Count(r => Ticket(r) != "") >= 1, string.Join(" | ", results));
        Assert.All(results.Where(r => Ticket(r) == ""), r => Assert.Equal("sso_conflict", Error(r)));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ArcvoraDbContext>().Users.CountAsync(u => u.Email == email));
    }

    [Fact]
    public async Task A_missing_default_workspace_refuses_instead_of_creating_orphans()
    {
        factory.Services.GetRequiredService<IOptions<OidcOptions>>().Value.DefaultWorkspaceId = Guid.NewGuid().ToString();
        Assert.Equal("sso_not_allowed", Error(await Login("sub-orphan", $"yetim-{Guid.NewGuid():N}@firma.com")));
    }
}

public class SsoDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Everything_is_off_and_hidden_by_default()
    {
        var api = new Api(factory.CreateClient());
        var p = await api.GetAsync("/api/auth/providers");
        Assert.False(p["oidc"].GetBoolean());
        Assert.False(p.Body.TryGetProperty("oidcLabel", out _));
        foreach (var path in new[] { "/api/auth/oidc/login", "/api/auth/oidc/callback?code=x&state=y" })
        {
            var r = await api.GetAsync(path);
            Assert.True(r.Is(HttpStatusCode.NotFound), path + " " + r);
            Assert.Equal("sso_disabled", r.Str("code"));
        }
        Assert.True((await api.PostAsync("/api/auth/oidc/ticket", new { ticket = "x" })).Is(HttpStatusCode.NotFound));
    }
}
