namespace Arcvora.Core.Files;

public enum FileKind { Pdf, Png, Jpeg, Webp }

/// <summary>İskelet: içerik tanıma ve ad temizleme (henüz uygulanmadı).</summary>
public static class FileSniffer
{
    public const int MaxNameLength = 100;
    public static FileKind? Detect(ReadOnlySpan<byte> head) => null;
    public static string ContentType(this FileKind kind) => "";
    public static string Extension(this FileKind kind) => "";
    public static string SafeName(string? raw, FileKind kind) => "";
}
