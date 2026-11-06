using System.Net;
using Arcvora.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace Arcvora.Integration.Tests;

public sealed class SmtpApiFactory : ApiFactory
{
    public SmtpSink Sink { get; } = new();
    protected override bool UseRealSmtp => true;
    protected override IReadOnlyDictionary<string, string> ExtraSettings => new Dictionary<string, string>
    {
        ["Email:Host"] = "127.0.0.1", ["Email:Port"] = Sink.Port.ToString(), ["Email:Security"] = "none", ["Email:FromName"] = "ARCVORA Apps",
        ["Email:TimeoutSeconds"] = "5",
    };

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        Sink.Dispose();
    }
}

/// <summary>G4: gerçek SMTP yolu — MailKit, soket, MIME kodlaması, Türkçe karakterler, alıcı reddi ve bağlantı kopması.</summary>
public class SmtpEmailTests(SmtpApiFactory factory) : IClassFixture<SmtpApiFactory>
{
    private async Task<(Api Admin, Api Boss, Api Emp, Guid App)> SetupAsync(string bossName = "Müdür Şükrü Öztürk")
    {
        var admin = await Api.RegisterAsync(factory);
        var boss = await admin.NewUserAsync(factory, "app_user", displayName: bossName);
        var emp = await admin.NewUserAsync(factory, "app_user", displayName: "Çalışan Can", managerId: boss.UserId);
        var app = await admin.PublishedAppAsync(AppsKit.LeaveDef(), "İzin Talebi", emp.UserId);
        return (admin, boss, emp, app);
    }

    [Fact]
    public async Task A_real_smtp_conversation_delivers_a_multipart_utf8_message_with_the_right_headers()
    {
        var (_, boss, emp, app) = await SetupAsync();
        var req = (await emp.SubmitAsync(app, AppsKit.Leave()))["summary"].GetProperty("id").GetGuid();
        await factory.RunEmailAsync();

        var received = factory.Sink.Mails.Single(m => m.To.Contains(boss.Email));
        Assert.Equal("noreply@firma.test", received.From);
        var msg = received.Parse();
        Assert.Equal("ARCVORA Apps", msg.From.Mailboxes.Single().Name);
        Assert.Equal("Müdür Şükrü Öztürk", msg.To.Mailboxes.Single().Name);           // Türkçe adlar başlıkta doğru kodlanır
        Assert.Contains("Onayınız bekleniyor", msg.Subject);
        Assert.Contains("Çalışan Can", msg.TextBody);
        Assert.Contains($"https://apps.test/#/requests/{req}", msg.TextBody);
        Assert.Contains($"href=\"https://apps.test/#/requests/{req}\"", msg.HtmlBody);
        Assert.True(msg.Date.Year >= 2026);
        Assert.NotEmpty(msg.MessageId);
    }

    [Fact]
    public async Task A_rejected_recipient_fails_permanently_at_once()
    {
        var (admin, boss, emp, app) = await SetupAsync();
        factory.Sink.RejectRecipient = a => a == boss.Email;
        await emp.SubmitAsync(app, AppsKit.Leave());
        await factory.RunEmailAsync();
        factory.Sink.RejectRecipient = _ => false;
        var failed = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/email/outbox?status=failed");
        var row = failed.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == boss.Email);
        Assert.Equal(1, row.GetProperty("attempts").GetInt32());
        Assert.Contains("reddetti", row.GetProperty("lastError").GetString());
        Assert.DoesNotContain(factory.Sink.Mails, m => m.To.Contains(boss.Email));
    }

    [Fact]
    public async Task Dropped_connections_are_retried_until_the_mail_is_delivered()
    {
        var (admin, boss, emp, app) = await SetupAsync();
        factory.Sink.DropNextConnections = 2;
        await emp.SubmitAsync(app, AppsKit.Leave());
        await factory.RunEmailAsync();
        Assert.Single(factory.Sink.Mails, m => m.To.Contains(boss.Email));
        var sent = await admin.GetAsync($"/api/workspaces/{admin.WorkspaceId}/email/outbox?status=sent");
        Assert.Equal(3, sent.Body.EnumerateArray().First(m => m.GetProperty("to").GetString() == boss.Email).GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task An_unreachable_server_ends_in_failed_with_a_safe_message_and_the_test_mail_reports_it()
    {
        var (admin, _, _, _) = await SetupAsync();
        var port = factory.Sink.Port;
        factory.Sink.DropNextConnections = 10;
        var bad = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/email/test");
        Assert.True(bad.Is(HttpStatusCode.BadGateway), bad.ToString());
        Assert.Equal("email_failed", bad.Str("code"));
        Assert.DoesNotContain("127.0.0.1", bad.ToString());                               // sunucu adresi/ayrıntı sızmaz
        factory.Sink.DropNextConnections = 0;
        Assert.True((await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/email/test")).Is(HttpStatusCode.OK));
        Assert.True(port > 0);
    }

    [Fact]
    public async Task The_test_mail_travels_over_real_smtp()
    {
        var (admin, _, _, _) = await SetupAsync();
        var ok = await admin.PostAsync($"/api/workspaces/{admin.WorkspaceId}/email/test");
        Assert.True(ok.Is(HttpStatusCode.OK), ok.ToString());
        var msg = factory.Sink.Mails.Last(m => m.To.Contains(admin.Email)).Parse();
        Assert.Contains("Test", msg.Subject);
    }

    [Fact]
    public async Task Hostile_text_arrives_escaped_in_html_over_the_wire()
    {
        var (admin, boss, emp, app) = await SetupAsync();
        var r = await emp.SubmitAsync(app, new { start = "2026-11-10", end = "2026-11-11", kind = "Yıllık", note = "<script>alert(1)</script>" });
        Assert.True(r.Is(HttpStatusCode.Created));
        var id = r["summary"].GetProperty("id").GetGuid();
        await boss.PostAsync($"/api/tasks/{await boss.MyPendingTaskAsync(id)}/reject", new { comment = "<img src=x onerror=alert(1)>\r\nBcc: x@evil.com" });
        await factory.RunEmailAsync();
        var msg = factory.Sink.Mails.Last(m => m.To.Contains(emp.Email)).Parse();
        Assert.DoesNotContain("<img", msg.HtmlBody);
        Assert.Contains("&lt;img", msg.HtmlBody);
        Assert.Single(msg.To.Mailboxes);
        Assert.Empty(msg.Bcc);
        _ = admin;
    }
}
