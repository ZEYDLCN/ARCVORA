using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arcvora.Infrastructure.Jobs;
using Arcvora.Infrastructure.Services;
using static Arcvora.Integration.Tests.AppsKit;

namespace Arcvora.Integration.Tests;

/// <summary>Faz D: onay → muhasebe kaydı (outbox), webhook teslimi, kuyruk ekranı, CSV ve erişim.</summary>
public class AccountingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Hook = "https://muhasebe.example.com/hook";
    private const string Secret = "super-secret-signing-key-0123";

    private static string Url(Api a, string rest = "") => $"/api/workspaces/{a.WorkspaceId}/accounting{rest}";

    private async Task<(Api Admin, Api Boss, Api Worker, Guid App)> SetupAsync(object? def = null)
    {
        factory.Webhook.Reset();
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, displayName: "Yönetici");
        var worker = await admin.NewUserAsync(factory, displayName: "Çalışan", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(def ?? ExpenseDef(), "Masraf", worker.UserId);
        return (admin, boss, worker, app);
    }

    private static async Task<Guid> ApprovedAsync(Api worker, Api boss, Guid app, decimal amount = 250.5m, string description = "Taksi", string comment = "uygun")
    {
        var r = await worker.SubmitAsync(app, new { amount, description });
        Assert.True(r.Is(HttpStatusCode.Created), r.ToString());
        var id = Guid.Parse(r["summary"].GetProperty("id").GetString()!);
        var done = await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id)}/approve", new { comment });
        Assert.Equal("approved", done["summary"].GetProperty("status").GetString());
        return id;
    }

    private static async Task ConfigureWebhookAsync(Api admin, bool secret = true)
    {
        var r = await admin.PutAsync(Url(admin, "/settings"), new { webhookUrl = Hook, webhookSecret = secret ? Secret : null });
        Assert.True(r.Is(HttpStatusCode.OK), r.ToString());
    }

    private static async Task<JsonElement> OnlyEntryAsync(Api who, Guid workspace)
    {
        var list = await who.GetAsync($"/api/workspaces/{workspace}/accounting/entries");
        Assert.True(list.Is(HttpStatusCode.OK), list.ToString());
        return list.Body[0];
    }

    // ------------------------------------------------------------------ outbox

    [Fact]
    public async Task Approval_queues_exactly_one_pending_entry_with_the_full_payload()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var requestId = await ApprovedAsync(worker, boss, app);

        var list = await admin.GetAsync(Url(admin, "/entries"));
        Assert.Equal(1, list.Body.GetArrayLength());
        var e = list.Body[0];
        Assert.Equal("pending", e.GetProperty("status").GetString());
        Assert.Equal("masraf", e.GetProperty("category").GetString());
        Assert.Equal(requestId.ToString(), e.GetProperty("requestId").GetString());

        var p = e.GetProperty("payload");
        Assert.Equal(e.GetProperty("number").GetString(), p.GetProperty("number").GetString());
        Assert.Equal(worker.Email, p.GetProperty("requester").GetProperty("email").GetString());
        Assert.Equal("Çalışan", p.GetProperty("requester").GetProperty("name").GetString());
        var amount = p.GetProperty("fields").EnumerateArray().First(f => f.GetProperty("key").GetString() == "amount");
        Assert.Equal(250.5m, amount.GetProperty("value").GetDecimal());
        Assert.Equal("TRY", amount.GetProperty("currency").GetString());
        var approval = p.GetProperty("approvals")[0];
        Assert.Equal("Yönetici onayı", approval.GetProperty("step").GetString());
        Assert.Equal("Yönetici", approval.GetProperty("by").GetProperty("name").GetString());
        Assert.Equal("uygun", approval.GetProperty("comment").GetString());

        var detail = await admin.GetAsync($"/api/requests/{requestId}");
        Assert.Contains("accounting_queued", detail.EventKinds());
    }

    [Fact]
    public async Task Requests_that_are_rejected_cancelled_or_from_apps_without_accounting_queue_nothing()
    {
        var (admin, boss, worker, app) = await SetupAsync();

        var rejected = await worker.SubmitAsync(app, new { amount = 10 });
        var rejectedId = Guid.Parse(rejected["summary"].GetProperty("id").GetString()!);
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(rejectedId)}/reject", new { comment = "hayır" });
        var cancelled = await worker.SubmitAsync(app, new { amount = 11 });
        await worker.PostAsync($"/api/requests/{cancelled["summary"].GetProperty("id").GetString()}/cancel");

        var noAccounting = await admin.PublishedAppAsync(ExpenseDef(prefix: "AVN", accounting: false), "Avans (muhasebesiz)", worker.UserId);
        await ApprovedAsync(worker, boss, noAccounting);

        Assert.Equal(0, (await admin.GetAsync(Url(admin, "/entries"))).Body.GetArrayLength());
    }

    [Fact]
    public async Task A_request_that_completes_instantly_because_every_step_is_skipped_is_queued_too()
    {
        var (admin, _, worker, app) = await SetupAsync(ExpenseDef(approvalAbove: 1000));
        var r = await worker.SubmitAsync(app, new { amount = 100 });       // 100 ≤ 1000 → adım atlanır
        Assert.Equal("approved", r["summary"].GetProperty("status").GetString());
        Assert.Equal(0, r["tasks"].GetArrayLength());
        Assert.Equal(1, (await admin.GetAsync(Url(admin, "/entries"))).Body.GetArrayLength());
    }

    // ----------------------------------------------------------------- erişim

    [Fact]
    public async Task Only_admins_and_the_accounting_group_can_use_the_queue()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var accountant = await admin.NewUserAsync(factory, displayName: "Muhasebeci");
        var outsider = await Api.RegisterAsync(factory);
        var requestId = await ApprovedAsync(worker, boss, app);
        var entryId = (await OnlyEntryAsync(admin, admin.WorkspaceId)).GetProperty("id").GetString();

        // Grup tanımlanmadan: yalnızca master admin.
        foreach (var who in new[] { worker, boss, accountant })
        {
            Assert.True((await who.GetAsync(Url(admin, "/entries"))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.GetAsync(Url(admin, "/export.csv"))).Is(HttpStatusCode.Forbidden));
            Assert.True((await who.PostAsync(Url(admin, $"/entries/{entryId}/process"))).Is(HttpStatusCode.Forbidden));
        }
        Assert.True((await outsider.GetAsync(Url(admin, "/entries"))).Is(HttpStatusCode.NotFound));

        // Muhasebe grubu tanımlanınca üyesi görür, işler ve talebi açabilir; başkaları hâlâ giremez.
        var group = (await admin.NewGroupAsync("Muhasebe", accountant.UserId)).Id();
        Assert.True((await admin.PutAsync(Url(admin, "/settings"), new { accountingGroupId = group })).Is(HttpStatusCode.OK));
        Assert.True((await accountant.GetAsync(Url(admin, "/entries"))).Is(HttpStatusCode.OK));
        Assert.True((await accountant.GetAsync($"/api/requests/{requestId}")).Is(HttpStatusCode.OK));
        Assert.True((await boss.GetAsync(Url(admin, "/entries"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await worker.GetAsync(Url(admin, "/entries"))).Is(HttpStatusCode.Forbidden));

        // Muhasebeci muhasebe kaydı olmayan bir talebi göremez.
        var plain = await admin.PublishedAppAsync(ExpenseDef(prefix: "AVN", accounting: false), "Avans", worker.UserId);
        var plainId = await ApprovedAsync(worker, boss, plain);
        Assert.True((await accountant.GetAsync($"/api/requests/{plainId}")).Is(HttpStatusCode.NotFound));

        var me = await accountant.GetAsync("/api/me");
        Assert.True(me["workspaces"][0].GetProperty("isAccounting").GetBoolean());
        Assert.False((await worker.GetAsync("/api/me"))["workspaces"][0].GetProperty("isAccounting").GetBoolean());
    }

    [Fact]
    public async Task Settings_are_admin_only_validated_and_never_expose_the_secret()
    {
        var (admin, _, worker, _) = await SetupAsync();
        var accountant = await admin.NewUserAsync(factory);
        var group = (await admin.NewGroupAsync("Muhasebe", accountant.UserId)).Id();
        await admin.PutAsync(Url(admin, "/settings"), new { accountingGroupId = group });

        Assert.True((await worker.GetAsync(Url(admin, "/settings"))).Is(HttpStatusCode.Forbidden));
        Assert.True((await accountant.GetAsync(Url(admin, "/settings"))).Is(HttpStatusCode.Forbidden));       // muhasebe üyesi ayar değiştiremez
        Assert.True((await accountant.PutAsync(Url(admin, "/settings"), new { webhookUrl = Hook })).Is(HttpStatusCode.Forbidden));

        foreach (var bad in new[] { "ftp://x.example/hook", "not a url", "https://user:pw@x.example/hook", "javascript:alert(1)", "/relative" })
            Assert.Equal("invalid_url", (await admin.PutAsync(Url(admin, "/settings"), new { webhookUrl = bad })).Str("code"));
        Assert.Equal("invalid_secret", (await admin.PutAsync(Url(admin, "/settings"), new { webhookSecret = "kisa" })).Str("code"));
        Assert.Equal("invalid_group", (await admin.PutAsync(Url(admin, "/settings"), new { accountingGroupId = Guid.NewGuid() })).Str("code"));

        var ok = await admin.PutAsync(Url(admin, "/settings"), new { webhookUrl = Hook, webhookSecret = Secret });
        Assert.Equal(Hook, ok.Str("webhookUrl"));
        Assert.True(ok["hasSecret"].GetBoolean());
        Assert.DoesNotContain(Secret, ok.ToString());
        Assert.DoesNotContain(Secret, (await admin.GetAsync(Url(admin, "/settings"))).ToString());

        var cleared = await admin.PutAsync(Url(admin, "/settings"), new { clearWebhook = true, clearSecret = true, clearGroup = true });
        Assert.False(cleared.Body.TryGetProperty("webhookUrl", out _));
        Assert.False(cleared["hasSecret"].GetBoolean());
        Assert.False(cleared.Body.TryGetProperty("accountingGroupId", out _));
    }

    // ------------------------------------------------------------------ webhook

    [Fact]
    public async Task The_webhook_receives_the_signed_payload_once_and_the_entry_becomes_sent()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ConfigureWebhookAsync(admin);
        var requestId = await ApprovedAsync(worker, boss, app);
        var entry = await OnlyEntryAsync(admin, admin.WorkspaceId);

        Assert.Equal(1, await factory.RunAccountingAsync());
        var call = Assert.Single(factory.Webhook.Calls);
        Assert.Equal(Hook, call.Url);
        Assert.Equal(entry.GetProperty("id").GetString(), call.Headers["Idempotency-Key"]);
        Assert.Equal("accounting.entry", call.Headers["X-Arcvora-Event"]);
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(call.Body))).ToLowerInvariant();
        Assert.Equal(expected, call.Headers["X-Arcvora-Signature"]);
        using (var body = JsonDocument.Parse(call.Body))
        {
            Assert.Equal(entry.GetProperty("number").GetString(), body.RootElement.GetProperty("number").GetString());
            Assert.Equal(entry.GetProperty("id").GetString(), body.RootElement.GetProperty("entryId").GetString());
        }

        var after = await OnlyEntryAsync(admin, admin.WorkspaceId);
        Assert.Equal("sent", after.GetProperty("status").GetString());
        Assert.Equal(1, after.GetProperty("attempts").GetInt32());
        Assert.NotNull(after.GetProperty("sentAt").GetString());
        Assert.Contains("accounting_sent", (await admin.GetAsync($"/api/requests/{requestId}")).EventKinds());

        Assert.Equal(0, await factory.RunAccountingAsync());       // gönderilmiş kayıt tekrar gönderilmez
        Assert.Single(factory.Webhook.Calls);
    }

    [Fact]
    public async Task Without_a_secret_the_request_is_sent_unsigned()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ConfigureWebhookAsync(admin, secret: false);
        await ApprovedAsync(worker, boss, app);
        await factory.RunAccountingAsync();
        Assert.False(Assert.Single(factory.Webhook.Calls).Headers.ContainsKey("X-Arcvora-Signature"));
    }

    [Fact]
    public async Task Failures_are_retried_with_a_limit_then_marked_failed_and_can_be_retried_by_hand()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ConfigureWebhookAsync(admin);
        var requestId = await ApprovedAsync(worker, boss, app);
        factory.Webhook.StatusCode = 500;

        Assert.Equal(3, await factory.RunAccountingAsync());       // MaxAttempts = 3
        Assert.Equal(3, factory.Webhook.Calls.Count);
        var failed = await OnlyEntryAsync(admin, admin.WorkspaceId);
        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal(3, failed.GetProperty("attempts").GetInt32());
        Assert.Equal("HTTP 500", failed.GetProperty("lastError").GetString());
        Assert.Contains("accounting_failed", (await admin.GetAsync($"/api/requests/{requestId}")).EventKinds());
        Assert.Equal(0, await factory.RunAccountingAsync());       // başarısız kayıt kendiliğinden denenmez

        factory.Webhook.StatusCode = 200;
        var retried = await admin.PostAsync(Url(admin, $"/entries/{failed.GetProperty("id").GetString()}/retry"));
        Assert.True(retried.Is(HttpStatusCode.OK), retried.ToString());
        Assert.Equal("pending", retried.Str("status"));
        Assert.Equal(0, retried["attempts"].GetInt32());
        Assert.Equal(1, await factory.RunAccountingAsync());
        Assert.Equal("sent", (await OnlyEntryAsync(admin, admin.WorkspaceId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_network_error_is_recorded_without_losing_the_entry()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ConfigureWebhookAsync(admin);
        await ApprovedAsync(worker, boss, app);
        factory.Webhook.Throw = new HttpRequestException("bağlantı reddedildi");

        await factory.RunAccountingAsync(1);
        var e = await OnlyEntryAsync(admin, admin.WorkspaceId);
        Assert.Equal("pending", e.GetProperty("status").GetString());            // henüz deneme hakkı var
        Assert.Contains("HttpRequestException", e.GetProperty("lastError").GetString());
    }

    [Fact]
    public async Task Without_a_webhook_entries_stay_pending_and_are_handled_manually()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ApprovedAsync(worker, boss, app);
        var entry = await OnlyEntryAsync(admin, admin.WorkspaceId);
        var id = entry.GetProperty("id").GetString();

        Assert.Equal(0, await factory.RunAccountingAsync());
        Assert.Empty(factory.Webhook.Calls);
        var retry = await admin.PostAsync(Url(admin, $"/entries/{id}/retry"));
        Assert.True(retry.Is(HttpStatusCode.Conflict), retry.ToString());
        Assert.Equal("webhook_not_configured", retry.Str("code"));

        var done = await admin.PostAsync(Url(admin, $"/entries/{id}/process"), new { note = "Logo'ya girildi" });
        Assert.True(done.Is(HttpStatusCode.OK), done.ToString());
        Assert.Equal("processed", done.Str("status"));
        Assert.Equal("Logo'ya girildi", done.Str("note"));
        Assert.NotNull(done.Str("processedByName"));
        Assert.Equal("already_processed", (await admin.PostAsync(Url(admin, $"/entries/{id}/process"))).Str("code"));
    }

    [Fact]
    public async Task Processed_entries_are_never_sent_and_a_long_note_is_refused()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ApprovedAsync(worker, boss, app);
        var id = (await OnlyEntryAsync(admin, admin.WorkspaceId)).GetProperty("id").GetString();

        Assert.Equal("note_too_long", (await admin.PostAsync(Url(admin, $"/entries/{id}/process"), new { note = new string('x', 501) })).Str("code"));
        await admin.PostAsync(Url(admin, $"/entries/{id}/process"));
        await ConfigureWebhookAsync(admin);
        Assert.Equal(0, await factory.RunAccountingAsync());
        Assert.Empty(factory.Webhook.Calls);
        Assert.Equal("not_retryable", (await admin.PostAsync(Url(admin, $"/entries/{id}/retry"))).Str("code"));
    }

    [Fact]
    public async Task Concurrent_dispatchers_deliver_an_entry_only_once()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ConfigureWebhookAsync(admin);
        await ApprovedAsync(worker, boss, app);
        factory.Webhook.DelayMs = 200;

        var d = factory.Services.GetService(typeof(AccountingDispatcher)) as AccountingDispatcher;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => d!.RunOnceAsync(CancellationToken.None)));
        Assert.Single(factory.Webhook.Calls);
        Assert.Equal("sent", (await OnlyEntryAsync(admin, admin.WorkspaceId)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Entries_of_one_workspace_are_never_sent_to_another_workspaces_webhook()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        var other = await Api.RegisterAsync(factory);
        await other.PutAsync(Url(other, "/settings"), new { webhookUrl = "https://baska.example.com/hook" });
        await ApprovedAsync(worker, boss, app);     // admin'in çalışma alanında webhook yok

        Assert.Equal(0, await factory.RunAccountingAsync());
        Assert.Empty(factory.Webhook.Calls);
        Assert.Equal(0, (await other.GetAsync(Url(other, "/entries"))).Body.GetArrayLength());
        Assert.NotNull(admin);
    }

    // --------------------------------------------------------------- filtreler

    [Fact]
    public async Task The_queue_can_be_filtered_by_status_and_date_and_rejects_bad_filters()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ApprovedAsync(worker, boss, app);
        await ApprovedAsync(worker, boss, app, 99);
        var first = (await admin.GetAsync(Url(admin, "/entries"))).Body[1].GetProperty("id").GetString();
        await admin.PostAsync(Url(admin, $"/entries/{first}/process"));

        Assert.Equal(1, (await admin.GetAsync(Url(admin, "/entries?status=processed"))).Body.GetArrayLength());
        Assert.Equal(1, (await admin.GetAsync(Url(admin, "/entries?status=pending"))).Body.GetArrayLength());
        Assert.Equal(2, (await admin.GetAsync(Url(admin, "/entries?from=2020-01-01"))).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync(Url(admin, "/entries?from=2099-01-01"))).Body.GetArrayLength());
        Assert.Equal(0, (await admin.GetAsync(Url(admin, "/entries?to=2020-01-01"))).Body.GetArrayLength());
        Assert.Equal("invalid_status", (await admin.GetAsync(Url(admin, "/entries?status=bilinmeyen"))).Str("code"));
        Assert.Equal("invalid_date", (await admin.GetAsync(Url(admin, "/entries?from=dün"))).Str("code"));
    }

    // --------------------------------------------------------------------- CSV

    [Fact]
    public async Task CSV_export_is_utf8_with_bom_uses_semicolons_and_neutralises_formulas()
    {
        var (admin, boss, worker, app) = await SetupAsync();
        await ApprovedAsync(worker, boss, app, 1250.5m, "Taksi");
        await ApprovedAsync(worker, boss, app, 10, "Çay; şeker\nikinci satır");
        // Hücre başına gelen kullanıcı girdisi (işleyen not) formül olarak başlıyor.
        var first = (await admin.GetAsync(Url(admin, "/entries"))).Body[1].GetProperty("id").GetString();
        await admin.PostAsync(Url(admin, $"/entries/{first}/process"), new { note = "=HYPERLINK(\"http://kotu.example\";\"tikla\")" });

        var res = await admin.Http.GetAsync(Url(admin, "/export.csv"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/csv", res.Content.Headers.ContentType!.ToString());
        Assert.Contains("attachment", res.Content.Headers.ContentDisposition!.ToString());
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        var csv = Encoding.UTF8.GetString(bytes);

        Assert.Contains("Numara;Uygulama;Kategori;Aktarım durumu;Talep eden;E-posta", csv);
        Assert.Contains("Beklemede", csv);
        Assert.Contains("İşlendi", csv);
        Assert.Contains("Tutar=1250.5 TRY", csv);
        // Formül olarak başlayan hücrenin önüne ' konur: Excel çalıştırmaz.
        Assert.DoesNotContain(";=HYPERLINK", csv);
        Assert.DoesNotContain(";\"=HYPERLINK", csv);
        Assert.Contains("\"'=HYPERLINK(", csv);
        // Ayırıcı ve satır sonu içeren metin tırnaklanır (özet hücresi).
        Assert.Contains("\"10.00 TRY · Çay; şeker\nikinci satır\"", csv);

        var comma = await (await admin.Http.GetAsync(Url(admin, "/export.csv?delimiter=,&status=pending"))).Content.ReadAsStringAsync();
        Assert.Contains("Numara,Uygulama,Kategori", comma);
        Assert.Equal("invalid_delimiter", (await admin.GetAsync(Url(admin, "/export.csv?delimiter=tab"))).Str("code"));
    }

    // ------------------------------------------------------------ saf fonksiyonlar

    [Theory]
    [InlineData(1, 30, 30)]
    [InlineData(2, 30, 60)]
    [InlineData(3, 30, 120)]
    [InlineData(5, 30, 480)]
    [InlineData(40, 30, 21600)]       // 6 saatle sınırlı
    [InlineData(1, 0, 0)]
    public void Backoff_doubles_per_attempt_and_is_capped(int attempts, int baseSeconds, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), AccountingDispatcher.Backoff(attempts, baseSeconds));

    [Theory]
    [InlineData("düz metin", "düz metin")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+90 532", "'+90 532")]
    [InlineData("-5", "'-5")]
    [InlineData("@kullanici", "'@kullanici")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("he said \"hi\"", "\"he said \"\"hi\"\"\"")]
    [InlineData(null, "")]
    public void CSV_cells_are_quoted_and_formula_safe(string? value, string expected) =>
        Assert.Equal(expected, AccountingService.Cell(value, ';'));
}
