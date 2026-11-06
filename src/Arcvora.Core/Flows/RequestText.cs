using System.Text.Json;

namespace Arcvora.Core.Flows;

/// <summary>Talep durumu ve zaman çizelgesi için Türkçe metinler (PDF ve e-postada kullanılır; web'deki requestText.ts ile aynı cümleler).</summary>
public static class RequestText
{
    public static string StatusLabel(string status) => status switch
    {
        "in_review" => "İncelemede",
        "approved" => "Onaylandı",
        "rejected" => "Reddedildi",
        "cancelled" => "İptal edildi",
        _ => status,
    };

    public static string FallbackLabel(string? reason) => reason switch
    {
        "manager_missing" => "talep edenin yöneticisi tanımlı olmadığı için",
        "group_empty" => "grup boş olduğu için",
        "user_missing" => "seçilen kişi bulunamadığı için",
        "admin_missing" => "master admin bulunamadığı için",
        "only_approver_is_requester" => "tek onaylayıcı talep edenin kendisi olduğu için",
        "no_approver" => "onaylayıcı bulunamadığı için",
        "assignee_removed" => "önceki onaylayıcı çalışma alanından çıkarıldığı için",
        null or "" => "",
        _ => reason,
    };

    /// <summary>Zaman çizelgesi satırı: kimin ne yaptığını bir cümleyle anlatır.</summary>
    public static string Event(string kind, Guid? actorId, JsonElement? detail, IReadOnlyDictionary<Guid, string> people)
    {
        var who = actorId is { } a ? (people.TryGetValue(a, out var n) ? n : "Bir kullanıcı") : "Sistem";
        string Str(string name) => detail is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : "") : "";
        string Num(string name) => detail is { ValueKind: JsonValueKind.Object } d0 && d0.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal().ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') : "?";
        var step = Str("stepName");
        var comment = Str("comment");
        // Vekil karar verdiyse "Vekil, Müdür adına": kayıtta hem kararı veren hem adına karar verilen görünür.
        string Behalf() => Guid.TryParse(Str("onBehalfOf"), out var b) ? $", {(people.TryGetValue(b, out var bn) ? bn : "Bir kullanıcı")} adına" : "";

        switch (kind)
        {
            case "submitted": return $"{who} talebi oluşturdu.";
            case "step_started":
            {
                var names = new List<string>();
                if (detail is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("assignees", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var x in arr.EnumerateArray())
                        names.Add(Guid.TryParse(x.GetString(), out var id) && people.TryGetValue(id, out var nm) ? nm : "Bilinmeyen");
                var fb = Str("fallback");
                return $"“{step}” adımı başladı. Onaylayacak: {string.Join(", ", names)}{(fb == "" ? "" : $" ({FallbackLabel(fb)} master admin’e yönlendirildi)")}.";
            }
            case "step_skipped": return $"“{step}” adımı koşul sağlanmadığı için atlandı.";
            case "approved": return $"{who}{Behalf()} “{step}” adımını onayladı{(comment == "" ? "." : ": " + comment)}";
            case "rejected": return $"{who}{Behalf()} “{step}” adımında reddetti{(comment == "" ? "." : ": " + comment)}";
            case "reassigned":
            {
                string P(string key) => Guid.TryParse(Str(key), out var id) && people.TryGetValue(id, out var nm) ? nm : "Bir kullanıcı";
                return $"{who} “{step}” görevinin onaylayıcısını değiştirdi: {P("from")} → {P("to")}.";
            }
            case "cancelled": return $"{who} talebi iptal etti.";
            case "approver_removed":
            {
                var removed = Guid.TryParse(Str("removedId"), out var rid) && people.TryGetValue(rid, out var rn) ? rn : "Bir onaylayıcı";
                var handed = detail is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty("replacements", out var rep) && rep.ValueKind == JsonValueKind.Array && rep.GetArrayLength() > 0;
                return $"{removed} çalışma alanından çıkarıldığı için “{step}” adımı {(handed ? "master admin’e yönlendirildi" : "kalan onaylayıcılara bırakıldı")}.";
            }
            case "leave_reserved": return $"{Num("days")} gün izin {Str("year")} yılı bakiyenizden ayrıldı (kalan: {Num("balanceAfter")} gün).";
            case "leave_refunded": return $"{Num("days")} gün izin {Str("year")} yılı bakiyenize iade edildi.";
            case "completed": return "Talep onaylandı.";
            case "accounting_queued": return "Muhasebe kuyruğuna eklendi.";
            case "accounting_sent": return "Muhasebe sistemine gönderildi.";
            case "accounting_failed": return "Muhasebe sistemine gönderilemedi; muhasebe elle işleyebilir.";
            case "accounting_processed": { var note = Str("note"); return $"{who} muhasebede işledi{(note == "" ? "." : ": " + note)}"; }
            default: return kind;
        }
    }
}
