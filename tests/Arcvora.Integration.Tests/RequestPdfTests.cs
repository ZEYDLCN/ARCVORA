using System.Diagnostics;
using Arcvora.Infrastructure.Documents;
using PdfSharp.Pdf.IO;

namespace Arcvora.Integration.Tests;

/// <summary>Yazdırılabilir PDF: geçerlilik, Türkçe karakterler, sayfalama, görseller ve kötü girdiler.</summary>
public class RequestPdfTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 5, 10, 30, 0, TimeSpan.Zero);

    private static RequestPdfModel Sample(IReadOnlyList<PdfField>? fields = null, IReadOnlyList<PdfAttachment>? files = null,
        IReadOnlyList<PdfApprovalRow>? approvals = null, IReadOnlyList<PdfTimelineRow>? timeline = null, string status = "approved") =>
        new("IZN-2026-0007", "İzin Talebi", "Örnek Gıda Sanayi A.Ş.", status, status == "approved" ? "Onaylandı" : "İncelemede", "Çağlar Öztürk", T0, status == "approved" ? T0.AddHours(5) : null, 3,
            fields ?? [new("İzin türü", "Yıllık"), new("Başlangıç", "10.11.2026"), new("Tutar", "1.234,50 TRY")],
            approvals ?? [new("Yönetici onayı", "Şükrü Ğünay", "Onayladı", T0.AddHours(2), "Uygun, iyi tatiller.")],
            timeline ?? [new(T0, "Çağlar Öztürk talebi oluşturdu."), new(T0.AddHours(2), "Şükrü Ğünay “Yönetici onayı” adımını onayladı: Uygun, iyi tatiller.")],
            files ?? [], "ABCD-1234-EF56", T0.AddDays(1));

    /// <summary>Geliştirme yardımcısı: ARCVORA_PDF_DUMP klasörü verilirse üretilen PDF'ler gözle incelemek için oraya yazılır.</summary>
    private static byte[] Dump(string name, byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("ARCVORA_PDF_DUMP") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".pdf"), pdf);
        }
        return pdf;
    }

    private static (bool Ok, string Text) Extract(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"arcvora-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, pdf);
            var psi = new ProcessStartInfo("pdftotext", $"-layout \"{path}\" -") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            if (p is null) return (false, "");
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return (p.ExitCode == 0, text);
        }
        catch (System.ComponentModel.Win32Exception) { return (false, ""); }      // pdftotext kurulu değil: metin denetimleri atlanır
        finally { File.Delete(path); }
    }

    private static int Pages(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }

    [Fact]
    public void Builds_a_valid_pdf_with_title_and_embedded_font()
    {
        var pdf = Dump("basic", RequestPdf.Build(Sample()));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.Contains("%%EOF", System.Text.Encoding.ASCII.GetString(pdf, pdf.Length - 16, 16));
        Assert.Equal(1, Pages(pdf));
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Contains("IZN-2026-0007", doc.Info.Title);
        Assert.True(pdf.Length > 50_000, "yazı tipi alt kümesi gömülmemiş görünüyor: " + pdf.Length);      // alt küme birkaç on KB tutar
    }

    [Fact]
    public void Turkish_characters_and_formatted_values_are_readable()
    {
        var (ok, text) = Extract(RequestPdf.Build(Sample()));
        if (!ok) return;
        foreach (var expected in new[] { "IZN-2026-0007", "İzin Talebi", "Çağlar Öztürk", "Şükrü Ğünay", "Örnek Gıda Sanayi A.Ş.", "1.234,50 TRY", "Uygun, iyi tatiller.", "ABCD-1234-EF56", "ONAYLANDI", "Onayladı" })
            Assert.Contains(expected, text);
    }

    [Fact]
    public void Every_page_has_the_verification_code_and_page_numbers()
    {
        var many = Enumerable.Range(1, 90).Select(i => new PdfField($"Alan {i}", $"Değer {i} " + new string('x', 40))).ToList();
        var pdf = Dump("many-pages", RequestPdf.Build(Sample(many)));
        var pages = Pages(pdf);
        Assert.True(pages >= 2, pages.ToString());
        var (ok, text) = Extract(pdf);
        if (!ok) return;
        Assert.Equal(pages, text.Split('\f', StringSplitOptions.RemoveEmptyEntries).Length);
        for (var i = 1; i <= pages; i++) Assert.Contains($"Sayfa {i}/{pages}", text);
        Assert.Equal(pages, System.Text.RegularExpressions.Regex.Matches(text, "ABCD-1234-EF56").Count);
        Assert.Contains("Alan 90", text);
    }

    [Fact]
    public void Long_unbroken_text_wraps_inside_the_page()
    {
        var pdf = RequestPdf.Build(Sample([new("Açıklama", new string('Ø', 700)), new("Not", string.Join(' ', Enumerable.Repeat("kelime", 300))), new("Satırlı", "bir\niki\n\nüç")], approvals: [], timeline: []));
        Assert.True(Pages(pdf) >= 1);
        var (ok, text) = Extract(pdf);
        if (!ok) return;
        Assert.All(text.Split('\n'), line => Assert.True(line.Length < 220, "satır taştı: " + line.Length));
        Assert.Equal(700, text.Count(c => c == 'Ø'));            // hiçbir karakter kaybolmadı
        Assert.Contains("üç", text);
    }

    [Fact]
    public void Hostile_text_never_breaks_generation()
    {
        var evil = "ctrl\u0000\u0001\u0007 bidi‮‏ tab\there emoji 😀🙂 <script>alert(1)</script> & \"quotes\" ' ';--";
        var pdf = RequestPdf.Build(Sample([new(evil, evil), new("", ""), new(new string('L', 400), "v")], timeline: [new(T0, evil)], approvals: [new(evil, evil, evil, T0, evil)]));
        Assert.True(Pages(pdf) >= 1);
        var (ok, text) = Extract(pdf);
        if (!ok) return;
        Assert.Contains("<script>alert(1)</script>", text);         // metin olarak basılır, hiçbir şey çalıştırılmaz
        Assert.DoesNotContain('‮', text);
    }

    [Fact]
    public void Images_are_embedded_and_corrupt_or_huge_ones_are_skipped_not_fatal()
    {
        var plain = RequestPdf.Build(Sample(files: [new("fis.png", "Fiş", FilesKit.Png().Length, "abc", null)]));
        var withImage = Dump("image", RequestPdf.Build(Sample(files: [new("fis.png", "Fiş", FilesKit.Png().Length, "abc", FilesKit.Png())])));
        Assert.True(withImage.Length > plain.Length, "görsel gömülmedi");

        var corrupt = RequestPdf.Build(Sample(files: [new("bozuk.png", "Fiş", 10, "abc", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])]));
        Assert.True(Pages(corrupt) >= 1);
        var (ok, text) = Extract(corrupt);
        if (ok) Assert.Contains("bozuk.png", text);                 // listede yine de görünür
    }

    [Fact]
    public void Attachment_list_shows_names_sizes_and_a_short_checksum()
    {
        var (ok, text) = Extract(RequestPdf.Build(Sample(files: [new("otel-faturası.pdf", "Fiş / fatura", 52_430, "0123456789abcdef0123456789abcdef", null)])));
        if (!ok) return;
        Assert.Contains("otel-faturası.pdf", text);
        Assert.Contains("51,2 KB", text);
        Assert.Contains("0123456789abcdef", text);
    }

    [Fact]
    public void Missing_sections_and_in_review_requests_still_render()
    {
        var pdf = RequestPdf.Build(Sample([], [], [], [], status: "in_review"));
        Assert.Equal(1, Pages(pdf));
        var (ok, text) = Extract(pdf);
        if (!ok) return;
        Assert.Contains("İNCELEMEDE", text);                          // durum rozeti Türkçe büyük harfle (i→İ)
    }

    [Fact]
    public void Times_are_shown_in_the_configured_offset()
    {
        var (ok, text) = Extract(RequestPdf.Build(Sample()));
        if (!ok) return;
        Assert.Contains("05.11.2026 13:30", text);                   // 10:30 UTC → UTC+03:00
        Assert.Contains("UTC+03:00", text);
    }
}
