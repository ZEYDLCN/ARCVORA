namespace Arcvora.Infrastructure.Demo;

/// <summary>İskelet (henüz uygulanmadı).</summary>
public static class SampleFiles
{
    public static byte[] ReceiptPng(int seed) => [];
    public static byte[] InvoicePdf(string company, string number, IReadOnlyList<(string Item, decimal Amount)> lines) => [];
}
