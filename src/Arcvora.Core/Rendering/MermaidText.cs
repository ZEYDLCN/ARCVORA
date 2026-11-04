using System.Text;

namespace Arcvora.Core.Rendering;

/// <summary>
/// Kullanıcı kaynaklı metnin Mermaid'e güvenli yazılması. Hiçbir çıktı satırı kullanıcı metniyle
/// yeni bir Mermaid yönergesi (click, %%{init}%% vb.) başlatamaz: satır sonları kaldırılır, tırnak ve
/// HTML işaretleri varlık kodlarına çevrilir.
/// </summary>
public static class MermaidText
{
    private const int MaxLabel = 120;

    /// <summary>Çift tırnak içindeki etiketler (flowchart / ER) için.</summary>
    public static string Label(string? s) => Escape(s, quoted: true);

    /// <summary>Sequence mesajları ve takma adları için (tırnaksız bağlam).</summary>
    public static string Seq(string? s) => Escape(s, quoted: false);

    /// <summary>CMP-001 → CMP_001. Yalnızca [A-Za-z0-9_] içerir.</summary>
    public static string NodeId(string id)
    {
        var sb = new StringBuilder(id.Length);
        foreach (var ch in id) sb.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        if (sb.Length == 0 || char.IsAsciiDigit(sb[0])) sb.Insert(0, 'n');
        return sb.ToString();
    }

    /// <summary>ER alan/tür adları için ASCII tanımlayıcı (Türkçe karakterler çevrilir).</summary>
    public static string Identifier(string? s, string fallback = "field")
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        var sb = new StringBuilder();
        foreach (var ch in s.Trim())
        {
            var mapped = ch switch
            {
                'ç' => 'c', 'Ç' => 'C', 'ğ' => 'g', 'Ğ' => 'G', 'ı' => 'i', 'İ' => 'I',
                'ö' => 'o', 'Ö' => 'O', 'ş' => 's', 'Ş' => 'S', 'ü' => 'u', 'Ü' => 'U',
                _ => ch,
            };
            sb.Append(char.IsAsciiLetterOrDigit(mapped) ? mapped : '_');
        }
        var id = sb.ToString().Trim('_');
        if (id.Length == 0) return fallback;
        if (char.IsAsciiDigit(id[0])) id = "f_" + id;
        return id;
    }

    private static string Escape(string? s, bool quoted)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var sb = new StringBuilder(s.Length + 8);
        var prevSpace = false;
        foreach (var ch in s.Trim())
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
            {
                if (!prevSpace) sb.Append(' ');
                prevSpace = true;
                continue;
            }
            prevSpace = false;
            switch (ch)
            {
                case '#': sb.Append("#35;"); break;
                case '"': sb.Append("#34;"); break;
                case '<': sb.Append("#60;"); break;
                case '>': sb.Append("#62;"); break;
                case '&': sb.Append("#38;"); break;
                case '`': sb.Append('\''); break;
                case '\\': sb.Append('/'); break;
                case ';': sb.Append(quoted ? ";" : ","); break;
                case '%' when !quoted: sb.Append("#37;"); break;
                default: sb.Append(ch); break;
            }
        }

        var result = sb.ToString();
        return result.Length <= MaxLabel ? result : result[..(MaxLabel - 3)] + "...";
    }
}
