namespace Arcvora.Core.Text;

/// <summary>
/// Arama için Türkçe harfleri ASCII'ye katlar: ı→i, İ→I, ş→s, Ş→S, ğ→g, Ğ→G, ü→u, Ü→U, ö→o, Ö→O, ç→c, Ç→C.
/// Kullanıcılar çoğu kez Türkçe harfsiz yazar ("sirket") veya büyük/küçük harfi karıştırır ("ISIL" ↔ "Işıl"); PostgreSQL'in ILIKE'ı bu harfleri
/// eşlemez. Hem aranan metin hem de (veritabanında aynı harf değişimiyle) aranan sütun katlanır, sonra ILIKE büyük/küçük harfi halleder.
/// Başka alfabelerin harflerine dokunulmaz. Veritabanı tarafındaki karşılığı <c>ReportService</c>'teki Replace zinciridir; ikisi birlikte değişmelidir.
/// </summary>
public static class SearchFold
{
    public const string From = "ıİşŞğĞüÜöÖçÇ";
    public const string To = "iIsSgGuUoOcC";

    public static string Ascii(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var at = From.IndexOf(chars[i]);
            if (at >= 0) chars[i] = To[at];
        }
        return new string(chars);
    }
}
