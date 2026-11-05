using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Arcvora.Infrastructure.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Arcvora.Infrastructure.Demo;

/// <summary>
/// Demo verisi için örnek ekler: fiş görünümlü bir PNG ve basit bir fatura PDF'i. Dış bağımlılık yok; içerik tamamen sentetiktir
/// (gerçek bir kişi/firma verisi içermez) ve belgede "DEMO" yazar.
/// </summary>
public static class SampleFiles
{
    private const int W = 300, H = 440;

    // ----------------------------------------------------------------------- PNG

    /// <summary>Fiş benzeri 300×440 görsel: renkli başlık, "metin" çizgileri, toplam satırı ve barkod. Aynı tohum → aynı bayt.</summary>
    public static byte[] ReceiptPng(int seed)
    {
        var rnd = new Random(seed);
        var px = new byte[H * W * 3];
        Fill(px, 0, 0, W, H, 252, 252, 250);
        var hue = new (byte R, byte G, byte B)[] { (37, 99, 235), (21, 128, 61), (180, 83, 9), (126, 34, 206), (190, 18, 60) }[Math.Abs(seed) % 5];
        Fill(px, 0, 0, W, 56, hue.R, hue.G, hue.B);
        Fill(px, W / 2 - 30, 14, 60, 28, 255, 255, 255);                      // "logo"
        var y = 76;
        for (var i = 0; i < 9; i++, y += 22)                                  // ürün satırları
        {
            Fill(px, 22, y, 90 + rnd.Next(0, 80), 7, 70, 76, 86);
            Fill(px, W - 22 - 44, y, 44, 7, 70, 76, 86);
        }
        Fill(px, 22, y + 2, W - 44, 2, 160, 165, 172);                        // ayırıcı
        y += 16;
        Fill(px, 22, y, 70, 10, 20, 24, 30);                                  // TOPLAM
        Fill(px, W - 22 - 66, y, 66, 10, 20, 24, 30);
        y += 34;
        for (var x = 26; x < W - 26;)                                         // barkod
        {
            var bar = rnd.Next(1, 4);
            Fill(px, x, y, bar, 56, 20, 24, 30);
            x += bar + rnd.Next(1, 4);
        }
        Fill(px, 60, H - 40, W - 120, 6, 160, 165, 172);                      // "DEMO" şeridi
        return Encode(W, H, px);
    }

    private static void Fill(byte[] px, int x, int y, int w, int h, byte r, byte g, byte b)
    {
        for (var yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
            for (var xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++)
            {
                var i = (yy * W + xx) * 3;
                px[i] = r; px[i + 1] = g; px[i + 2] = b;
            }
    }

    private static byte[] Encode(int w, int h, byte[] rgb)
    {
        var raw = new byte[h * (1 + w * 3)];
        for (var y = 0; y < h; y++)
            Buffer.BlockCopy(rgb, y * w * 3, raw, y * (1 + w * 3) + 1, w * 3);      // süzgeç baytı 0 (None)

        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)w);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 2;                                                  // 8 bit, RGB
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc32(typed));
        s.Write(len);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    // ----------------------------------------------------------------------- PDF

    public static byte[] InvoicePdf(string company, string number, IReadOnlyList<(string Item, decimal Amount)> lines)
    {
        PdfFonts.Install();
        using var doc = new PdfDocument();
        doc.Info.Title = $"{number} — DEMO fatura";
        doc.Info.Author = "ARCVORA Apps (demo)";
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(595.28);
        page.Height = XUnit.FromPoint(841.89);
        using (var g = XGraphics.FromPdfPage(page))
        {
            var title = new XFont(PdfFonts.Family, 22, XFontStyleEx.Bold);
            var bold = new XFont(PdfFonts.Family, 11, XFontStyleEx.Bold);
            var body = new XFont(PdfFonts.Family, 11, XFontStyleEx.Regular);
            var small = new XFont(PdfFonts.Family, 9, XFontStyleEx.Regular);
            var ink = new XSolidBrush(XColor.FromArgb(28, 32, 38));
            var muted = new XSolidBrush(XColor.FromArgb(104, 112, 124));
            double y = 56;
            g.DrawString("FATURA", title, ink, new XPoint(56, y), XStringFormats.TopLeft);
            g.DrawString("ÖRNEK BELGE · DEMO", bold, new XSolidBrush(XColor.FromArgb(185, 28, 28)), new XPoint(595 - 56 - 140, y + 8), XStringFormats.TopLeft);
            y += 40;
            g.DrawString(PdfClean(company), bold, ink, new XPoint(56, y), XStringFormats.TopLeft);
            g.DrawString($"Fatura no: {PdfClean(number)}", body, muted, new XPoint(56, y + 18), XStringFormats.TopLeft);
            y += 56;
            g.DrawLine(new XPen(XColor.FromArgb(214, 219, 226), 0.8), 56, y, 595 - 56, y);
            y += 12;
            decimal total = 0;
            foreach (var (item, amount) in lines)
            {
                g.DrawString(PdfClean(item), body, ink, new XPoint(56, y), XStringFormats.TopLeft);
                var text = Money(amount);
                g.DrawString(text, body, ink, new XPoint(595 - 56 - g.MeasureString(text, body).Width, y), XStringFormats.TopLeft);
                total += amount;
                y += 22;
            }
            g.DrawLine(new XPen(XColor.FromArgb(28, 32, 38), 1.2), 56, y + 2, 595 - 56, y + 2);
            y += 12;
            g.DrawString("TOPLAM", bold, ink, new XPoint(56, y), XStringFormats.TopLeft);
            var t = Money(total);
            g.DrawString(t, bold, ink, new XPoint(595 - 56 - g.MeasureString(t, bold).Width, y), XStringFormats.TopLeft);
            g.DrawString("Bu belge ARCVORA Apps demo verisi olarak üretilmiştir; gerçek bir fatura değildir.", small, muted, new XPoint(56, 800), XStringFormats.TopLeft);
        }
        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static string PdfClean(string s) => RequestPdf.Clean(s);

    private static string Money(decimal v)
    {
        var tr = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = ".", NumberGroupSizes = [3] };
        return v.ToString("N2", tr) + " TRY";
    }
}
