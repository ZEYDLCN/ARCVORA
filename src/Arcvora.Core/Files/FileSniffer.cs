using System.Globalization;
using System.Text;

namespace Arcvora.Core.Files;

public enum FileKind { Pdf, Png, Jpeg, Webp }

/// <summary>
/// Yüklenen dosyanın gerçek türünü içeriğinden (sihirli baytlar) belirler ve kullanıcı adını güvenli hâle getirir.
/// İstemcinin bildirdiği içerik türüne/uzantıya güvenilmez. SVG, HTML, arşiv ve yürütülebilir dosyalar bilerek desteklenmez.
/// </summary>
public static class FileSniffer
{
    public const int MaxNameLength = 100;
    private const int MinLength = 12;
    private const string DefaultName = "ek";
    private const string BadNameChars = "<>:\"|?*";

    public static FileKind? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length < MinLength) return null;
        if (head[..5].SequenceEqual("%PDF-"u8)) return FileKind.Pdf;
        if (head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return FileKind.Png;
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return FileKind.Jpeg;
        if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return FileKind.Webp;
        return null;
    }

    public static string ContentType(this FileKind kind) => kind switch
    {
        FileKind.Pdf => "application/pdf",
        FileKind.Png => "image/png",
        FileKind.Jpeg => "image/jpeg",
        _ => "image/webp",
    };

    public static string Extension(this FileKind kind) => kind switch
    {
        FileKind.Pdf => ".pdf",
        FileKind.Png => ".png",
        FileKind.Jpeg => ".jpg",
        _ => ".webp",
    };

    /// <summary>
    /// Yolu, tehlikeli/denetim/çift yönlü karakterleri ve yanlış uzantıyı atar; uzantıyı gerçek türe göre koyar; uzunluğu sınırlar.
    /// </summary>
    public static string SafeName(string? raw, FileKind kind)
    {
        var ext = kind.Extension();
        if (string.IsNullOrWhiteSpace(raw)) return DefaultName + ext;

        var name = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && LooksLikeExtension(name.AsSpan(dot + 1))) name = name[..dot];

        var sb = new StringBuilder(name.Length);
        foreach (var rune in name.EnumerateRunes())
        {
            if (rune.Value == 0xFFFD || BadNameChars.Contains((char)(rune.IsBmp ? rune.Value : 0))) continue;
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                    or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
                    continue;
                case UnicodeCategory.SpaceSeparator:
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    continue;
                default:
                    sb.Append(rune.ToString());
                    break;
            }
        }

        var stem = sb.ToString().Trim(' ', '.');
        var room = MaxNameLength - ext.Length;
        if (stem.Length > room)
        {
            stem = stem[..room];
            if (char.IsHighSurrogate(stem[^1])) stem = stem[..^1];      // eşleşmemiş vekil karakter bırakma
            stem = stem.Trim(' ', '.');
        }
        return (stem.Length == 0 ? DefaultName : stem) + ext;
    }

    /// <summary>"pdf", "jpeg", "exe" uzantıdır; "11" (12.11 içindeki) değildir: en az bir harf, en çok 5 harf/rakam.</summary>
    private static bool LooksLikeExtension(ReadOnlySpan<char> s)
    {
        if (s.Length is < 1 or > 5) return false;
        var hasLetter = false;
        foreach (var c in s)
        {
            if (!char.IsLetterOrDigit(c)) return false;
            if (char.IsLetter(c)) hasLetter = true;
        }
        return hasLetter;
    }
}
