using System.Text;

namespace Arcvora.Integration.Tests;

/// <summary>Testlerde kullanılan küçük ama gerçek biçimde dosyalar.</summary>
public static class FilesKit
{
    /// <summary>Geçerli 1x1 piksel PNG.</summary>
    public static byte[] Png() => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static byte[] Pdf(string text = "Fis") =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n% {text}\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    public static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    public static byte[] Html() => Encoding.UTF8.GetBytes("<html><body><script>alert('xss')</script></body></html>");

    /// <summary>PNG imzası + verilen boyuta kadar sıfır (boyut sınırı testleri için).</summary>
    public static byte[] BigPng(int bytes)
    {
        var data = new byte[bytes];
        Png().AsSpan(0, 12).CopyTo(data);
        return data;
    }
}
