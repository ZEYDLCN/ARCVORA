using System.Buffers.Binary;
using System.IO.Compression;
using Arcvora.Core.Files;
using Arcvora.Infrastructure.Demo;
using PdfSharp.Pdf.IO;

namespace Arcvora.Integration.Tests;

/// <summary>Demo verisi için üretilen örnek fiş (PNG) ve fatura (PDF): gerçekten geçerli dosyalar olmalı.</summary>
public class SampleFilesTests
{
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void The_receipt_is_a_structurally_valid_png_that_the_sniffer_accepts()
    {
        var png = SampleFiles.ReceiptPng(1);
        Assert.Equal(FileKind.Png, FileSniffer.Detect(png));

        // Parça parça doğrula: imza, her parçanın CRC'si, IHDR boyutları, IDAT açılınca h*(1+w*3) bayt.
        var pos = 8;
        int w = 0, h = 0;
        var idat = new MemoryStream();
        var sawEnd = false;
        while (pos < png.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var body = png.AsSpan(pos + 8, len);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + len, 4));
            Assert.Equal(crc, Crc32(png.AsSpan(pos + 4, 4 + len)));
            if (type == "IHDR") { w = (int)BinaryPrimitives.ReadUInt32BigEndian(body[..4]); h = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..8]); Assert.Equal(8, body[8]); Assert.Equal(2, body[9]); }
            if (type == "IDAT") idat.Write(body);
            if (type == "IEND") sawEnd = true;
            pos += 12 + len;
        }
        Assert.True(sawEnd);
        Assert.True(w >= 200 && h >= 300, $"{w}x{h}");
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        Assert.Equal(h * (1 + w * 3), raw.Length);
    }

    [Fact]
    public void Different_seeds_make_different_receipts_and_the_same_seed_is_stable()
    {
        Assert.NotEqual(SampleFiles.ReceiptPng(1), SampleFiles.ReceiptPng(2));
        Assert.Equal(SampleFiles.ReceiptPng(7), SampleFiles.ReceiptPng(7));
        Assert.True(SampleFiles.ReceiptPng(1).Length < 100_000);
    }

    [Fact]
    public void The_invoice_is_a_valid_one_page_pdf_marked_as_demo()
    {
        var pdf = SampleFiles.InvoicePdf("Örnek Otel Turizm A.Ş.", "FTR-2026-0042", [("Konaklama (2 gece)", 3200m), ("Kahvaltı", 450.5m)]);
        Assert.Equal(FileKind.Pdf, FileSniffer.Detect(pdf));
        using var ms = new MemoryStream(pdf);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
        Assert.Equal(1, doc.PageCount);
        Assert.Contains("DEMO", doc.Info.Title);
    }
}
