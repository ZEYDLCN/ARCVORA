using Arcvora.Core.Notifications;

namespace Arcvora.Core.Tests;

/// <summary>E-posta şablonları: Türkçe metin, güvenli HTML, başlık enjeksiyonu ve bağlantı denetimi.</summary>
public class EmailTemplatesTests
{
    private static MailContext Ctx(string? link = "https://apps.firma.com/#/requests/abc", string requester = "Can Yıldız", string summary = "10.11.2026 · 12.11.2026 · Yıllık") =>
        new("Mehmet Demir", "IZN-2026-0007", "İzin Talebi", requester, summary, link);

    [Fact]
    public void A_new_task_names_the_request_requester_step_and_link()
    {
        var m = EmailTemplates.TaskAssigned(Ctx(), "Yönetici onayı");
        Assert.Equal("[ARCVORA] Onayınız bekleniyor: IZN-2026-0007 · İzin Talebi", m.Subject);
        foreach (var part in new[] { "Merhaba Mehmet Demir", "IZN-2026-0007", "İzin Talebi", "Can Yıldız", "Yönetici onayı", "10.11.2026 · 12.11.2026 · Yıllık", "https://apps.firma.com/#/requests/abc" })
        {
            Assert.Contains(part, m.Text);
            Assert.Contains(part.Replace("&", "&amp;"), m.Html);        // yalnızca HTML'e özel karakterler kaçışlanır; Türkçe harfler aynen kalır
        }
        Assert.Contains("href=\"https://apps.firma.com/#/requests/abc\"", m.Html);
    }

    [Fact]
    public void Reminders_say_how_long_the_task_has_been_waiting()
    {
        var m = EmailTemplates.TaskReminder(Ctx(), "Yönetici onayı", 3);
        Assert.Contains("Hatırlatma", m.Subject);
        Assert.Contains("3 gündür", m.Text);
        Assert.Contains("3 gündür", EmailTemplates.TaskReminder(Ctx(), "x", 3).Html);
        Assert.Contains("1 gündür", EmailTemplates.TaskReminder(Ctx(), "x", 1).Text);
        Assert.Contains("Bugün", EmailTemplates.TaskReminder(Ctx(), "x", 0).Text.Replace("bugün", "Bugün"));
    }

    [Fact]
    public void Results_tell_the_requester_what_happened_and_why()
    {
        var ok = EmailTemplates.RequestApproved(Ctx());
        Assert.Contains("onaylandı", ok.Subject);
        Assert.Contains("onaylandı", ok.Text);

        var no = EmailTemplates.RequestRejected(Ctx(), "Yönetici onayı", "Bütçe yok.\nAy sonunda tekrar iletin.");
        Assert.Contains("reddedildi", no.Subject);
        Assert.Contains("Bütçe yok.\nAy sonunda tekrar iletin.", no.Text);
        Assert.Contains("Bütçe yok.<br>Ay sonunda tekrar iletin.", no.Html);

        var cancelled = EmailTemplates.RequestCancelled(Ctx(), "Ayşe Kaya");
        Assert.Contains("iptal", cancelled.Subject);
        Assert.Contains("Ayşe Kaya", cancelled.Text);
    }

    [Fact]
    public void Delegation_mails_name_both_people_and_the_dates()
    {
        var m = EmailTemplates.DelegationGranted("Veli", "Mehmet Demir", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 14), "Yıllık izin", "https://apps.firma.com/#/inbox");
        Assert.Contains("vekil", m.Subject);
        foreach (var part in new[] { "Veli", "Mehmet Demir", "10.11.2026", "14.11.2026", "Yıllık izin", "https://apps.firma.com/#/inbox" }) Assert.Contains(part, m.Text);
        var noReason = EmailTemplates.DelegationGranted("Veli", "Mehmet Demir", new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 14), null, null);
        Assert.DoesNotContain("Açıklama", noReason.Text);
        var revoked = EmailTemplates.DelegationRevoked("Veli", "Mehmet Demir", "Mehmet Demir");
        Assert.Contains("iptal", revoked.Subject);
    }

    [Fact]
    public void Invite_and_test_mails_never_contain_a_password()
    {
        var invite = EmailTemplates.UserInvited("Can", "can@firma.com", "https://apps.firma.com/");
        Assert.Contains("can@firma.com", invite.Text);
        Assert.Contains("Geçici parolanız size yöneticiniz tarafından ayrıca iletilecek", invite.Text);
        Assert.DoesNotContain("Parola:", invite.Text);
        Assert.Contains("ARCVORA", EmailTemplates.Test("Ayşe").Subject);
    }

    [Fact]
    public void Hostile_text_is_escaped_in_html_and_kept_literal_in_plain_text()
    {
        var evil = "<script>alert(1)</script><img src=x onerror=alert(2)> & \"q\" 'a'";
        var m = EmailTemplates.TaskAssigned(Ctx(requester: evil, summary: evil), evil);
        Assert.DoesNotContain("<script>", m.Html);
        Assert.DoesNotContain("<img", m.Html);
        Assert.Contains("&lt;script&gt;", m.Html);
        Assert.Contains(evil, m.Text);
        var rejected = EmailTemplates.RequestRejected(Ctx(), evil, evil);
        Assert.DoesNotContain("<script>", rejected.Html);
    }

    [Fact]
    public void Subjects_never_contain_line_breaks_or_control_characters()
    {
        var m = EmailTemplates.TaskAssigned(new("Ad", "IZN-1\r\nBcc: kurban@x.com", "App\r\nSubject: hacked\u0000", "R", "S", null), "Adım");
        Assert.DoesNotContain('\r', m.Subject);
        Assert.DoesNotContain('\n', m.Subject);
        Assert.DoesNotContain('\0', m.Subject);
        Assert.True(m.Subject.Length <= 160);
        var longApp = EmailTemplates.RequestApproved(new("Ad", "IZN-1", new string('A', 1000), "R", "S", null));
        Assert.True(longApp.Subject.Length <= 160, longApp.Subject.Length.ToString());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("ftp://x/y")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_http_and_https_links_are_ever_rendered(string? link)
    {
        var m = EmailTemplates.TaskAssigned(Ctx(link), "Adım");
        Assert.DoesNotContain("<a ", m.Html);
        Assert.DoesNotContain("javascript:", m.Html);
        Assert.DoesNotContain("data:", m.Html);
        Assert.StartsWith("<", m.Html.TrimStart());
    }

    [Fact]
    public void Quotes_in_links_cannot_break_out_of_the_href()
    {
        var m = EmailTemplates.TaskAssigned(Ctx("https://x.com/\" onmouseover=\"alert(1)"), "Adım");
        Assert.DoesNotContain("onmouseover=\"alert", m.Html);
    }

    [Fact]
    public void Very_long_comments_are_shortened()
    {
        var m = EmailTemplates.RequestRejected(Ctx(), "Adım", new string('x', 5000));
        Assert.True(m.Text.Length < 3000);
        Assert.Contains("…", m.Text);
    }

    [Fact]
    public void The_footer_explains_how_to_stop_notifications()
    {
        var m = EmailTemplates.TaskAssigned(Ctx(), "Adım");
        Assert.Contains("Bildirimleri", m.Text);
        Assert.Contains("ARCVORA Apps", m.Html);
    }

    [Fact]
    public void Link_helper_builds_request_links_from_a_public_base_url()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal("https://apps.firma.com/#/requests/11111111-1111-1111-1111-111111111111", EmailTemplates.RequestLink("https://apps.firma.com", id));
        Assert.Equal("https://apps.firma.com/#/requests/11111111-1111-1111-1111-111111111111", EmailTemplates.RequestLink("https://apps.firma.com/", id));
        Assert.Equal("http://10.0.0.5:8080/arcvora/#/requests/11111111-1111-1111-1111-111111111111", EmailTemplates.RequestLink("http://10.0.0.5:8080/arcvora/", id));
        Assert.Null(EmailTemplates.RequestLink("", id));
        Assert.Null(EmailTemplates.RequestLink(null, id));
        Assert.Null(EmailTemplates.RequestLink("javascript:alert(1)", id));
        Assert.Equal("https://apps.firma.com/#/inbox", EmailTemplates.PageLink("https://apps.firma.com", "inbox"));
    }
}
