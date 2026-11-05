using System.Reflection;
using PdfSharp.Fonts;

namespace Arcvora.Infrastructure.Documents;

/// <summary>
/// PDFsharp platformdan bağımsız sürümü sistem yazı tiplerine bakmaz; Türkçe karakterleri (ğ ş ı İ ö ç ü) karşılayan DejaVu Sans
/// derlemeye gömülüdür. Böylece PDF her sunucuda (Linux/Docker dahil) aynı çıkar ve yazı tipi PDF'e gömülür.
/// </summary>
public sealed class PdfFonts : IFontResolver
{
    public const string Family = "DejaVu Sans";
    private static readonly object Gate = new();
    private static bool _installed;

    public static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;
            GlobalFontSettings.FontResolver = new PdfFonts();
            _installed = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? "DejaVuSans-Bold" : "DejaVuSans");

    public byte[]? GetFont(string faceName)
    {
        using var s = typeof(PdfFonts).Assembly.GetManifestResourceStream($"Fonts.{faceName}.ttf")
                      ?? throw new InvalidOperationException($"Gömülü yazı tipi bulunamadı: {faceName}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
