using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Arcvora.Infrastructure.Documents;

/// <summary>
/// Talep formunu yazdırılabilir A4 PDF olarak çizer: üst bilgi, talep bilgileri, ekler (görseller gömülü), onay zinciri,
/// zaman çizelgesi; her sayfada doğrulama kodu ve sayfa numarası. Yazı tipi (DejaVu Sans) PDF'e gömülür.
/// Kullanıcı metinleri yalnızca metin olarak basılır; denetim/çift yönlü karakterler ve yazı tipinde olmayan semboller temizlenir.
/// </summary>
public static class RequestPdf
{
    private const double PageW = 595.28, PageH = 841.89, Margin = 46, Bottom = 64;
    private const double ContentW = PageW - 2 * Margin;
    private const int MaxImages = 8;

    private static readonly XColor Ink = XColor.FromArgb(28, 32, 38);
    private static readonly XColor Muted = XColor.FromArgb(104, 112, 124);
    private static readonly XColor Line = XColor.FromArgb(214, 219, 226);
    private static readonly XColor Accent = XColor.FromArgb(37, 99, 235);
    private static readonly XColor Soft = XColor.FromArgb(244, 246, 249);

    public static byte[] Build(RequestPdfModel m)
    {
        PdfFonts.Install();
        using var doc = new PdfDocument();
        doc.Info.Title = $"{Clean(m.Number)} — {Clean(m.AppName)}";
        doc.Info.Author = "ARCVORA Apps";
        doc.Info.Creator = "ARCVORA Apps";
        doc.Info.Subject = $"Talep formu · {Clean(m.WorkspaceName)}";
        doc.Info.CreationDate = m.GeneratedAt.UtcDateTime;

        using var w = new Writer(doc, m);
        w.Header();
        w.Section("Talep bilgileri");
        w.KeyValues(m.Fields.Select(f => (f.Label, f.Value)).Prepend(("Talep eden", m.RequesterName)).Append(("Uygulama sürümü", $"v{m.VersionNumber}")));
        if (m.Attachments.Count > 0) { w.Section("Ekler"); w.Attachments(m.Attachments); }
        w.Section("Onay akışı");
        w.Approvals(m.Approvals);
        if (m.Timeline.Count > 0) { w.Section("Zaman çizelgesi"); w.Timeline(m.Timeline); }
        w.Footers();

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    // ------------------------------------------------------------ metin temizliği

    /// <summary>Denetim ve çift yönlü karakterleri atar, sekmeyi boşluğa çevirir, yazı tipinde olmayan astral sembolleri '?' yapar.</summary>
    public static string Clean(string? text, bool keepNewlines = false)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n') { sb.Append(keepNewlines ? '\n' : ' '); continue; }
            if (rune.Value == '\t') { sb.Append("    "); continue; }
            if (rune.Value == 0xFFFD) continue;
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                    or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator:
                    continue;
            }
            sb.Append(rune.IsBmp ? rune.ToString() : "?");
        }
        return sb.ToString();
    }

    /// <summary>Türkçe büyük harf (i→İ, ı→I). Kültür verisine bağlı değildir: globalleştirme-değişmez kipinde (Docker) de çalışır.</summary>
    private static string TrUpper(string s) => s.Replace('i', 'İ').Replace('ı', 'I').ToUpperInvariant();

    private static string Stamp(DateTimeOffset at, int offsetMinutes) =>
        at.ToOffset(TimeSpan.FromMinutes(offsetMinutes)).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    private static string Size(long bytes)
    {
        var tr = new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." };
        return bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? (bytes / 1024.0).ToString("0.#", tr) + " KB" : (bytes / 1024.0 / 1024.0).ToString("0.#", tr) + " MB";
    }

    // ----------------------------------------------------------------- çizici

    private sealed class Writer : IDisposable
    {
        private readonly List<IDisposable> _keep = [];       // görseller PDF kaydedilene kadar açık kalmalı
        private readonly PdfDocument _doc;
        private readonly RequestPdfModel _m;
        private readonly XGraphics _measure = XGraphics.CreateMeasureContext(new XSize(PageW, PageH), XGraphicsUnit.Point, XPageDirection.Downwards);
        private readonly XFont _body = new(PdfFonts.Family, 9.5, XFontStyleEx.Regular);
        private readonly XFont _bold = new(PdfFonts.Family, 9.5, XFontStyleEx.Bold);
        private readonly XFont _small = new(PdfFonts.Family, 8, XFontStyleEx.Regular);
        private readonly XFont _smallBold = new(PdfFonts.Family, 8, XFontStyleEx.Bold);
        private readonly XFont _h2 = new(PdfFonts.Family, 11.5, XFontStyleEx.Bold);
        private PdfPage _page = null!;
        private XGraphics _g = null!;
        private double _y;
        private int _images;

        public Writer(PdfDocument doc, RequestPdfModel m)
        {
            _doc = doc;
            _m = m;
            NewPage();
        }

        private void NewPage()
        {
            _g?.Dispose();
            _page = _doc.AddPage();
            _page.Width = XUnit.FromPoint(PageW);
            _page.Height = XUnit.FromPoint(PageH);
            _g = XGraphics.FromPdfPage(_page);
            _y = Margin;
            if (_doc.PageCount > 1)
            {
                Text(Clean(_m.Number), _smallBold, Muted, Margin, _y);
                Text(Clean(_m.AppName), _small, Muted, PageW - Margin - Width(Clean(_m.AppName), _small), _y);
                _y += 16;
                _g.DrawLine(new XPen(Line, 0.6), Margin, _y, PageW - Margin, _y);
                _y += 12;
            }
        }

        private void Ensure(double height)
        {
            if (_y + height > PageH - Bottom) NewPage();
        }

        private double Width(string s, XFont f) => _measure.MeasureString(s, f).Width;
        private void Text(string s, XFont f, XColor c, double x, double y) =>
            _g.DrawString(s, f, new XSolidBrush(c), new XPoint(x, y), XStringFormats.TopLeft);

        /// <summary>Kelime kelime sarar; sığmayan tek kelime karakter karakter bölünür; \n korunur.</summary>
        private List<string> Wrap(string text, XFont f, double width)
        {
            var lines = new List<string>();
            foreach (var para in Clean(text, keepNewlines: true).Split('\n'))
            {
                if (para.Length == 0) { lines.Add(""); continue; }
                var cur = new StringBuilder();
                foreach (var word in para.Split(' ', StringSplitOptions.None))
                {
                    var piece = word;
                    while (Width(piece, f) > width && piece.Length > 1)
                    {
                        if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                        var n = piece.Length;
                        while (n > 1 && Width(piece[..n], f) > width) n--;
                        if (n > 0 && char.IsHighSurrogate(piece[n - 1])) n--;
                        lines.Add(piece[..n]);
                        piece = piece[n..];
                    }
                    var candidate = cur.Length == 0 ? piece : cur + " " + piece;
                    if (Width(candidate, f) <= width) { cur.Clear(); cur.Append(candidate); }
                    else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(piece); }
                }
                lines.Add(cur.ToString());
            }
            while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        public void Header()
        {
            Text("ARCVORA Apps", _smallBold, Accent, Margin, _y);
            var ws = Clean(_m.WorkspaceName);
            Text(ws, _small, Muted, PageW - Margin - Width(ws, _small), _y);
            _y += 22;

            Text(Clean(_m.Number), new XFont(PdfFonts.Family, 21, XFontStyleEx.Bold), Ink, Margin, _y);
            var (fg, bg) = _m.Status switch
            {
                "approved" => (XColor.FromArgb(21, 128, 61), XColor.FromArgb(220, 252, 231)),
                "rejected" => (XColor.FromArgb(185, 28, 28), XColor.FromArgb(254, 226, 226)),
                "cancelled" => (XColor.FromArgb(75, 85, 99), XColor.FromArgb(229, 231, 235)),
                _ => (XColor.FromArgb(180, 83, 9), XColor.FromArgb(254, 243, 199)),
            };
            var label = TrUpper(Clean(_m.StatusLabel));
            var lw = Width(label, _smallBold) + 22;
            _g.DrawRoundedRectangle(new XSolidBrush(bg), PageW - Margin - lw, _y + 5, lw, 20, 10, 10);
            Text(label, _smallBold, fg, PageW - Margin - lw + 11, _y + 10);
            _y += 32;

            Text(Clean(_m.AppName), new XFont(PdfFonts.Family, 12, XFontStyleEx.Regular), Muted, Margin, _y);
            _y += 18;
            var meta = $"Oluşturma: {Stamp(_m.CreatedAt, _m.UtcOffsetMinutes)}" + (_m.CompletedAt is { } done ? $"   ·   Sonuç: {Stamp(done, _m.UtcOffsetMinutes)}" : "");
            Text(meta, _small, Muted, Margin, _y);
            _y += 16;
            _g.DrawLine(new XPen(Accent, 1.4), Margin, _y, PageW - Margin, _y);
            _y += 14;
        }

        public void Section(string title)
        {
            Ensure(48);
            _y += 8;
            Text(title, _h2, Ink, Margin, _y);
            _y += 19;
        }

        public void KeyValues(IEnumerable<(string Label, string Value)> rows)
        {
            const double labelW = 150, gap = 10;
            var valueW = ContentW - labelW - gap;
            foreach (var (label, value) in rows)
            {
                var l = Wrap(label, _bold, labelW);
                var v = Wrap(value, _body, valueW);
                var lines = Math.Max(l.Count, v.Count);
                var h = lines * 13 + 7;
                if (h > PageH - Bottom - Margin - 40) { v = v.Take((int)((PageH - Bottom - Margin - 60) / 13)).ToList(); lines = Math.Max(l.Count, v.Count); h = lines * 13 + 7; }
                Ensure(h);
                for (var i = 0; i < l.Count; i++) Text(l[i], _bold, Muted, Margin, _y + 3 + i * 13);
                for (var i = 0; i < v.Count; i++) Text(v[i], _body, Ink, Margin + labelW + gap, _y + 3 + i * 13);
                _y += h;
                _g.DrawLine(new XPen(Line, 0.5), Margin, _y, PageW - Margin, _y);
            }
        }

        public void Attachments(IReadOnlyList<PdfAttachment> files)
        {
            foreach (var f in files)
            {
                var title = Wrap($"{f.FileName}", _bold, ContentW - 4);
                Ensure(title.Count * 13 + 26);
                for (var i = 0; i < title.Count; i++) Text(title[i], _bold, Ink, Margin, _y + i * 13);
                _y += title.Count * 13;
                var sum = f.Sha256.Length >= 16 ? f.Sha256[..16] : f.Sha256;
                Text($"{Clean(f.Label)} · {Size(f.Size)} · SHA-256: {sum}…", _small, Muted, Margin, _y);
                _y += 15;
                if (f.Image is { Length: > 0 } && _images < MaxImages) TryImage(f);
            }
        }

        private void TryImage(PdfAttachment f)
        {
            try
            {
                var bytes = f.Image!;
                var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);       // PDFsharp GetBuffer() ister
                _keep.Add(stream);
                var img = XImage.FromStream(stream);
                _keep.Add(img);
                var maxW = ContentW * 0.8;
                var maxH = 250.0;
                var scale = Math.Min(1.0, Math.Min(maxW / img.PointWidth, maxH / img.PointHeight));
                if (img.PointWidth <= 0 || img.PointHeight <= 0 || double.IsNaN(scale)) return;
                var w = img.PointWidth * scale;
                var h = img.PointHeight * scale;
                Ensure(h + 12);
                _g.DrawRectangle(new XPen(Line, 0.6), Margin - 2, _y - 2, w + 4, h + 4);
                _g.DrawImage(img, Margin, _y, w, h);
                _y += h + 12;
                _images++;
            }
            catch (Exception)
            {
                // Bozuk/desteklenmeyen görsel: yalnızca dosya adı listede kalır; PDF yine üretilir.
            }
        }

        public void Dispose()
        {
            foreach (var d in _keep) d.Dispose();
            _measure.Dispose();
        }

        public void Approvals(IReadOnlyList<PdfApprovalRow> rows)
        {
            if (rows.Count == 0)
            {
                Ensure(20);
                Text("Onay gerektirmedi.", _body, Muted, Margin, _y);
                _y += 18;
                return;
            }
            double[] cw = [108, 100, 60, 78, ContentW - 108 - 100 - 60 - 78];
            string[] heads = ["Adım", "Onaylayan", "Karar", "Tarih", "Yorum"];
            void Head()
            {
                Ensure(26);
                _g.DrawRectangle(new XSolidBrush(Soft), Margin, _y, ContentW, 20);
                var x = Margin;
                for (var i = 0; i < heads.Length; i++) { Text(heads[i], _smallBold, Muted, x + 5, _y + 6); x += cw[i]; }
                _y += 22;
            }
            Head();
            foreach (var r in rows)
            {
                var cells = new[]
                {
                    Wrap(r.Step, _body, cw[0] - 8), Wrap(r.Who, _body, cw[1] - 8), Wrap(r.Decision, _bold, cw[2] - 8),
                    Wrap(r.At is { } at ? Stamp(at, _m.UtcOffsetMinutes) : "—", _body, cw[3] - 8), Wrap(string.IsNullOrWhiteSpace(r.Comment) ? "—" : r.Comment!, _body, cw[4] - 8),
                };
                var h = cells.Max(c => c.Count) * 13 + 8;
                if (_y + h > PageH - Bottom) { NewPage(); Head(); }
                var x = Margin;
                for (var i = 0; i < cells.Length; i++)
                {
                    for (var j = 0; j < cells[i].Count; j++) Text(cells[i][j], i == 2 ? _bold : _body, i == 2 ? DecisionColor(r.Decision) : Ink, x + 5, _y + 4 + j * 13);
                    x += cw[i];
                }
                _y += h;
                _g.DrawLine(new XPen(Line, 0.5), Margin, _y, PageW - Margin, _y);
            }
            _y += 6;
        }

        private static XColor DecisionColor(string d) => d switch
        {
            "Onayladı" => XColor.FromArgb(21, 128, 61),
            "Reddetti" => XColor.FromArgb(185, 28, 28),
            _ => Muted,
        };

        public void Timeline(IReadOnlyList<PdfTimelineRow> rows)
        {
            const double stampW = 92;
            foreach (var r in rows)
            {
                var lines = Wrap(r.Text, _small, ContentW - stampW - 8);
                var h = lines.Count * 11.5 + 5;
                Ensure(h);
                Text(Stamp(r.At, _m.UtcOffsetMinutes), _small, Muted, Margin, _y);
                for (var i = 0; i < lines.Count; i++) Text(lines[i], _small, Ink, Margin + stampW + 8, _y + i * 11.5);
                _y += h;
            }
        }

        /// <summary>Tüm sayfalar bittikten sonra: doğrulama kodu, üretim zamanı ve "Sayfa i/n" (toplam bilindiği için).</summary>
        public void Footers()
        {
            _g.Dispose();
            var total = _doc.PageCount;
            var offset = TimeSpan.FromMinutes(_m.UtcOffsetMinutes);
            var zone = "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm");
            for (var i = 0; i < total; i++)
            {
                using var g = XGraphics.FromPdfPage(_doc.Pages[i], XGraphicsPdfPageOptions.Append);
                var y = PageH - 48;
                g.DrawLine(new XPen(Line, 0.6), Margin, y - 8, PageW - Margin, y - 8);
                g.DrawString($"Doğrulama kodu: {Clean(_m.VerificationCode)}", _smallBold, new XSolidBrush(Ink), new XPoint(Margin, y), XStringFormats.TopLeft);
                var right = $"Sayfa {i + 1}/{total}";
                g.DrawString(right, _small, new XSolidBrush(Muted), new XPoint(PageW - Margin - _measure.MeasureString(right, _small).Width, y), XStringFormats.TopLeft);
                g.DrawString($"Bu belge ARCVORA Apps tarafından {Stamp(_m.GeneratedAt, _m.UtcOffsetMinutes)} ({zone}) tarihinde oluşturuldu. Kodun geçerliliği sistemden sorgulanabilir.",
                    _small, new XSolidBrush(Muted), new XPoint(Margin, y + 13), XStringFormats.TopLeft);
            }
        }
    }
}
