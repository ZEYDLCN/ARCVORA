using System.Text;
using Arcvora.Core.Files;

namespace Arcvora.Core.Tests;

/// <summary>Yüklenen dosyanın gerçek türü içeriğinden anlaşılır (istemcinin söylediğine güvenilmez) ve ad güvenli hâle getirilir.</summary>
public class FileSnifferTests
{
    private static byte[] Bytes(params byte[] b) => b;
    private static byte[] Pad(byte[] head, int total = 64) { var r = new byte[Math.Max(total, head.Length)]; head.CopyTo(r, 0); return r; }

    [Fact]
    public void Pdf_png_jpeg_and_webp_are_recognised_by_their_magic_bytes()
    {
        Assert.Equal(FileKind.Pdf, FileSniffer.Detect(Pad(Encoding.ASCII.GetBytes("%PDF-1.7\n"))));
        Assert.Equal(FileKind.Png, FileSniffer.Detect(Pad(Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))));
        Assert.Equal(FileKind.Jpeg, FileSniffer.Detect(Pad(Bytes(0xFF, 0xD8, 0xFF, 0xE0))));
        Assert.Equal(FileKind.Webp, FileSniffer.Detect(Pad(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 "))));
    }

    [Theory]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("MZ\u0090\u0000\u0003")]                       // Windows yürütülebilir
    [InlineData("PK\u0003\u0004")]                              // zip / docx / jar
    [InlineData("GIF89a")]
    [InlineData("just some text")]
    [InlineData("  %PDF-1.4")]                                  // başta boşluk: sezgisel değil, katı kural
    public void Everything_else_is_rejected(string text) =>
        Assert.Null(FileSniffer.Detect(Pad(Encoding.Latin1.GetBytes(text))));

    [Fact]
    public void Empty_and_tiny_files_are_rejected()
    {
        Assert.Null(FileSniffer.Detect([]));
        Assert.Null(FileSniffer.Detect(Encoding.ASCII.GetBytes("%PDF")));
        Assert.Null(FileSniffer.Detect(Bytes(0xFF, 0xD8, 0xFF)));
    }

    [Fact]
    public void Riff_files_that_are_not_webp_are_rejected() =>
        Assert.Null(FileSniffer.Detect(Pad(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "))));

    [Fact]
    public void Each_kind_has_a_content_type_and_extension()
    {
        Assert.Equal(("application/pdf", ".pdf"), (FileKind.Pdf.ContentType(), FileKind.Pdf.Extension()));
        Assert.Equal(("image/png", ".png"), (FileKind.Png.ContentType(), FileKind.Png.Extension()));
        Assert.Equal(("image/jpeg", ".jpg"), (FileKind.Jpeg.ContentType(), FileKind.Jpeg.Extension()));
        Assert.Equal(("image/webp", ".webp"), (FileKind.Webp.ContentType(), FileKind.Webp.Extension()));
    }

    // ------------------------------------------------------------------- ad

    [Theory]
    [InlineData("fis.pdf", FileKind.Pdf, "fis.pdf")]
    [InlineData("Taksi Fişi 12.11.jpg", FileKind.Jpeg, "Taksi Fişi 12.11.jpg")]
    [InlineData("fis.exe", FileKind.Png, "fis.png")]                          // yanlış uzantı gerçeğiyle değişir
    [InlineData("fis", FileKind.Pdf, "fis.pdf")]
    [InlineData("FIS.PDF", FileKind.Pdf, "FIS.pdf")]
    [InlineData("scan.jpeg", FileKind.Jpeg, "scan.jpg")]
    [InlineData("..\\..\\windows\\system32\\evil.dll", FileKind.Png, "evil.png")]
    [InlineData("../../etc/passwd", FileKind.Pdf, "passwd.pdf")]
    [InlineData("C:\\Users\\ali\\fatura.pdf", FileKind.Pdf, "fatura.pdf")]
    [InlineData("  fatura   mayıs .pdf ", FileKind.Pdf, "fatura mayıs.pdf")]
    [InlineData("a<b>c:d\"e|f?g*h.pdf", FileKind.Pdf, "abcdefgh.pdf")]
    public void Names_lose_paths_dangerous_characters_and_wrong_extensions(string raw, FileKind kind, string expected) =>
        Assert.Equal(expected, FileSniffer.SafeName(raw, kind));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("/")]
    [InlineData(".pdf")]
    public void Empty_or_meaningless_names_fall_back_to_a_default(string? raw) =>
        Assert.Equal("ek.pdf", FileSniffer.SafeName(raw, FileKind.Pdf));

    [Fact]
    public void Control_and_bidi_override_characters_are_removed()
    {
        // U+202E (sağdan sola geçersiz kılma) "fisgpj.exe"yi "fisexe.jpg" gibi göstermek için kullanılır.
        var tricky = "fis\u202Egpj.exe";
        var safe = FileSniffer.SafeName(tricky, FileKind.Png);
        Assert.DoesNotContain('\u202E', safe);
        Assert.EndsWith(".png", safe);
        Assert.Equal("fis\u0000\r\nx.png".Replace("\u0000", "").Replace("\r\n", ""), FileSniffer.SafeName("fis\u0000\r\nx.pdf", FileKind.Png));
    }

    [Fact]
    public void Very_long_names_are_truncated_but_keep_the_extension()
    {
        var safe = FileSniffer.SafeName(new string('a', 500) + ".pdf", FileKind.Pdf);
        Assert.True(safe.Length <= FileSniffer.MaxNameLength, safe.Length.ToString());
        Assert.EndsWith(".pdf", safe);
    }

    [Fact]
    public void Truncation_never_splits_a_surrogate_pair()
    {
        var raw = new string('a', FileSniffer.MaxNameLength - 5) + "😀😀😀😀";
        var safe = FileSniffer.SafeName(raw, FileKind.Pdf);
        // Geçerli UTF-16 mi? Yetim vekil karakter olursa UTF-8'e dönüşümde '?' çıkar.
        Assert.Equal(safe, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(safe)));
    }
}
