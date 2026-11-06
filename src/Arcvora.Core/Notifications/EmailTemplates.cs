using System.Globalization;
using System.Text;

namespace Arcvora.Core.Notifications;

public sealed record MailContent(string Subject, string Text, string Html);

/// <param name="Link">Yalnızca http/https; geçersizse mailde bağlantı olmaz.</param>
public sealed record MailContext(string RecipientName, string Number, string AppName, string RequesterName, string Summary, string? Link);

/// <summary>
/// Bildirim e-postaları (Türkçe): düz metin + basit HTML. Kullanıcıdan gelen her metin HTML'de kaçışlanır; başlık satırlarından
/// satır sonu/denetim karakterleri atılır (başlık enjeksiyonu); bağlantı yalnızca http/https ise yazılır. Parola içeren mail yoktur.
/// </summary>
public static class EmailTemplates
{
    private const int MaxSubject = 150;
    private const int MaxComment = 500;
    private const string Brand = "ARCVORA Apps";

    // ------------------------------------------------------------ görev / talep

    /// <param name="onBehalfOf">Doluysa alıcı bu kişinin vekilidir: konu ve metin "vekâleten" olur.</param>
    public static MailContent TaskAssigned(MailContext c, string step, string? onBehalfOf = null) =>
        onBehalfOf is null
            ? Build(c.RecipientName, $"[ARCVORA] Onayınız bekleniyor: {c.Number} · {c.AppName}",
                [$"{c.RequesterName} tarafından açılan aşağıdaki talep onayınızı bekliyor."], Details(c, step), c.Link, "Talebi aç")
            : Build(c.RecipientName, $"[ARCVORA] Vekâleten onayınız bekleniyor: {c.Number} · {c.AppName}",
                [$"{c.RequesterName} tarafından açılan aşağıdaki talep, {onBehalfOf} adına vekâleten sizin kararınızı bekliyor."], Details(c, step), c.Link, "Talebi aç");

    public static MailContent TaskReminder(MailContext c, string step, int daysWaiting, string? onBehalfOf = null)
    {
        var waiting = daysWaiting <= 0 ? "Bugün size atanan bir talep henüz karara bağlanmadı." : $"Aşağıdaki talep {daysWaiting} gündür onayınızı bekliyor.";
        return onBehalfOf is null
            ? Build(c.RecipientName, $"[ARCVORA] Hatırlatma: {c.Number} · {c.AppName} onayınızı bekliyor", [waiting], Details(c, step), c.Link, "Talebi aç")
            : Build(c.RecipientName, $"[ARCVORA] Vekâleten hatırlatma: {c.Number} · {c.AppName} onayınızı bekliyor",
                [daysWaiting <= 0 ? $"{onBehalfOf} adına vekâleten karar vermeniz gereken bir talep bugün atandı ve henüz karara bağlanmadı."
                    : $"Aşağıdaki talep {daysWaiting} gündür {onBehalfOf} adına vekâleten sizin kararınızı bekliyor."], Details(c, step), c.Link, "Talebi aç");
    }

    public static MailContent RequestApproved(MailContext c) =>
        Build(c.RecipientName, $"[ARCVORA] Talebiniz onaylandı: {c.Number} · {c.AppName}",
            ["Talebiniz tüm onay adımlarından geçti ve onaylandı."], Details(c, null), c.Link, "Talebi aç");

    public static MailContent RequestRejected(MailContext c, string step, string? comment)
    {
        var lines = new List<(string, string)>(Details(c, step));
        if (!string.IsNullOrWhiteSpace(comment)) lines.Add(("Gerekçe", Shorten(comment.Trim(), MaxComment)));
        return Build(c.RecipientName, $"[ARCVORA] Talebiniz reddedildi: {c.Number} · {c.AppName}",
            ["Talebiniz reddedildi. Gerekçeyi aşağıda görebilir, gerekirse yeni bir talep açabilirsiniz."], lines, c.Link, "Talebi aç");
    }

    public static MailContent RequestCancelled(MailContext c, string byName) =>
        Build(c.RecipientName, $"[ARCVORA] Talebiniz iptal edildi: {c.Number} · {c.AppName}",
            [$"Talebiniz {byName} tarafından iptal edildi."], Details(c, null), c.Link, "Talebi aç");

    // ----------------------------------------------------------------- vekâlet

    public static MailContent DelegationGranted(string recipient, string fromName, DateOnly start, DateOnly end, string? reason, string? link)
    {
        var lines = new List<(string, string)> { ("Vekâlet veren", fromName), ("Dönem", $"{Day(start)} – {Day(end)}") };
        if (!string.IsNullOrWhiteSpace(reason)) lines.Add(("Açıklama", Shorten(reason.Trim(), 200)));
        return Build(recipient, $"[ARCVORA] {fromName} sizi vekil olarak belirledi",
            [$"{fromName}, belirtilen günler arasında onay görevlerini görmenizi ve karara bağlamanızı sağlayan bir vekâlet verdi. Görevler “Onay kutusu”nda “vekâleten” rozetiyle görünür."], lines, link, "Onay kutusunu aç");
    }

    public static MailContent DelegationRevoked(string recipient, string otherName, string byName) =>
        Build(recipient, $"[ARCVORA] Vekâlet iptal edildi: {otherName}",
            [$"{otherName} ile aranızdaki vekâlet {byName} tarafından iptal edildi; vekâlet yetkisi sona erdi."], [], null, null);

    // ------------------------------------------------------------- hesap / test

    public static MailContent UserInvited(string recipient, string email, string? link) =>
        Build(recipient, "[ARCVORA] Hesabınız oluşturuldu",
            ["Sizin için bir ARCVORA Apps hesabı oluşturuldu. Giriş için kullanacağınız e-posta adresi aşağıdadır.",
             "Geçici parolanız size yöneticiniz tarafından ayrıca iletilecek; ilk girişte kendi parolanızı belirlemeniz istenir. Parola bu e-postada gönderilmez."],
            [("E-posta", email)], link, "Giriş sayfasını aç");

    public static MailContent Test(string recipient) =>
        Build(recipient, "[ARCVORA] Test e-postası", ["Bu bir test iletisidir. Bunu görüyorsanız e-posta (SMTP) ayarlarınız çalışıyor."], [], null, null);

    // ---------------------------------------------------------------- bağlantı

    /// <summary>Talebin adresi: <c>{kök}/#/requests/{id}</c>. Kök http/https değilse veya boşsa null (mailde bağlantı olmaz).</summary>
    public static string? RequestLink(string? baseUrl, Guid id) => PageLink(baseUrl, $"requests/{id}");

    public static string? PageLink(string? baseUrl, string page)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        return $"{uri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/#/{page}";
    }

    // ------------------------------------------------------------------ yapı

    private static List<(string Label, string Value)> Details(MailContext c, string? step)
    {
        var l = new List<(string, string)> { ("Talep", $"{c.Number} · {c.AppName}"), ("Talep eden", c.RequesterName) };
        if (!string.IsNullOrWhiteSpace(c.Summary)) l.Add(("Özet", c.Summary));
        if (!string.IsNullOrWhiteSpace(step)) l.Add(("Adım", step));
        return l;
    }

    private static MailContent Build(string recipient, string subject, IReadOnlyList<string> intro, IReadOnlyList<(string Label, string Value)> details, string? link, string? linkLabel)
    {
        var url = SafeUrl(link);
        var text = new StringBuilder();
        text.Append("Merhaba ").Append(recipient).Append(",\n\n");
        foreach (var p in intro) text.Append(p).Append("\n\n");
        foreach (var (label, value) in details) text.Append(label).Append(": ").Append(value).Append('\n');
        if (details.Count > 0) text.Append('\n');
        if (url is not null) text.Append(linkLabel ?? "Aç").Append(": ").Append(url).Append("\n\n");
        text.Append("— ").Append(Brand).Append("\nBildirimleri hesabınızdaki “E-posta bildirimleri” seçeneğinden kapatabilirsiniz.\n");

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#1c2026;max-width:560px\">");
        html.Append("<p>Merhaba ").Append(H(recipient)).Append(",</p>");
        foreach (var p in intro) html.Append("<p>").Append(H(p)).Append("</p>");
        if (details.Count > 0)
        {
            html.Append("<table style=\"border-collapse:collapse;margin:12px 0\">");
            foreach (var (label, value) in details)
                html.Append("<tr><td style=\"padding:3px 14px 3px 0;color:#68707c;vertical-align:top\">").Append(H(label)).Append("</td><td style=\"padding:3px 0\">")
                    .Append(H(value)).Append("</td></tr>");
            html.Append("</table>");
        }
        if (url is not null)
            html.Append("<p><a href=\"").Append(H(url)).Append("\" style=\"display:inline-block;padding:9px 18px;background:#2563eb;color:#ffffff;text-decoration:none;border-radius:8px\">")
                .Append(H(linkLabel ?? "Aç")).Append("</a></p>");
        html.Append("<p style=\"color:#68707c;font-size:12px;margin-top:22px\">— ").Append(Brand)
            .Append("<br>Bildirimleri hesabınızdaki “E-posta bildirimleri” seçeneğinden kapatabilirsiniz.</p></div>");

        return new MailContent(Subject(subject), text.ToString(), html.ToString());
    }

    /// <summary>
    /// HTML kaçışı (metin ve öznitelik için güvenli): yalnızca &amp; &lt; &gt; " ' kaçışlanır, Türkçe karakterler olduğu gibi kalır (kaynak okunur);
    /// satır sonları &lt;br&gt; olur.
    /// </summary>
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;")
        .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");

    private static string? SafeUrl(string? url) =>
        url is not null && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.IsWellFormedOriginalString() ? u.AbsoluteUri : null;

    /// <summary>Başlık tek satırdır: denetim/satır sonu karakterleri atılır, boşluklar sadeleşir, uzunluk sınırlanır.</summary>
    private static string Subject(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(rune);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); continue; }
            if (cat == UnicodeCategory.SpaceSeparator) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); continue; }
            sb.Append(rune.ToString());
        }
        var one = sb.ToString().Trim();
        return Shorten(one, MaxSubject);
    }

    private static string Shorten(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = s[..(max - 1)];
        if (char.IsHighSurrogate(cut[^1])) cut = cut[..^1];
        return cut + "…";
    }

    private static string Day(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
