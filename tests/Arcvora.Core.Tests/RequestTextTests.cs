using System.Text.Json;
using Arcvora.Core.Flows;

namespace Arcvora.Core.Tests;

/// <summary>Zaman çizelgesi cümleleri (PDF ve e-postada kullanılır); web'deki requestText.ts ile aynı metinler (yorum varsa kullanıcının yazdığı gibi biter, nokta eklenmez).</summary>
public class RequestTextTests
{
    private static readonly Guid Ayse = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Mehmet = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly IReadOnlyDictionary<Guid, string> People = new Dictionary<Guid, string> { [Ayse] = "Ayşe Kaya", [Mehmet] = "Mehmet Demir" };
    private static JsonElement? D(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Text(string kind, Guid? actor = null, string? detail = null) => RequestText.Event(kind, actor, detail is null ? null : D(detail), People);

    [Fact]
    public void Statuses_have_turkish_labels()
    {
        Assert.Equal("İncelemede", RequestText.StatusLabel("in_review"));
        Assert.Equal("Onaylandı", RequestText.StatusLabel("approved"));
        Assert.Equal("Reddedildi", RequestText.StatusLabel("rejected"));
        Assert.Equal("İptal edildi", RequestText.StatusLabel("cancelled"));
        Assert.Equal("x", RequestText.StatusLabel("x"));
    }

    [Fact]
    public void Submitted_approved_rejected_and_cancelled_name_the_actor_and_comment()
    {
        Assert.Equal("Ayşe Kaya talebi oluşturdu.", Text("submitted", Ayse));
        Assert.Equal("Mehmet Demir “Yönetici onayı” adımını onayladı.", Text("approved", Mehmet, """{"stepName":"Yönetici onayı"}"""));
        Assert.Equal("Mehmet Demir “Yönetici onayı” adımını onayladı: Uygun", Text("approved", Mehmet, """{"stepName":"Yönetici onayı","comment":"Uygun"}"""));
        Assert.Equal("Mehmet Demir “GM” adımında reddetti: Bütçe yok", Text("rejected", Mehmet, """{"stepName":"GM","comment":"Bütçe yok"}"""));
        Assert.Equal("Ayşe Kaya talebi iptal etti.", Text("cancelled", Ayse));
    }

    [Fact]
    public void Unknown_or_missing_actors_are_described_gracefully()
    {
        Assert.Equal("Sistem talebi oluşturdu.", Text("submitted"));
        Assert.Equal("Bir kullanıcı talebi iptal etti.", Text("cancelled", Guid.NewGuid()));
    }

    [Fact]
    public void Step_started_lists_assignees_and_explains_a_fallback()
    {
        var plain = Text("step_started", null, $$"""{"stepName":"Yönetici","assignees":["{{Mehmet}}"]}""");
        Assert.Equal("“Yönetici” adımı başladı. Onaylayacak: Mehmet Demir.", plain);
        var fallback = Text("step_started", null, $$"""{"stepName":"Yönetici","assignees":["{{Ayse}}"],"fallback":"manager_missing"}""");
        Assert.Equal("“Yönetici” adımı başladı. Onaylayacak: Ayşe Kaya (talep edenin yöneticisi tanımlı olmadığı için master admin’e yönlendirildi).", fallback);
        Assert.Contains("Bilinmeyen", Text("step_started", null, $$"""{"stepName":"x","assignees":["{{Guid.NewGuid()}}"]}"""));
    }

    [Fact]
    public void Skipped_completed_and_accounting_events()
    {
        Assert.Equal("“GM” adımı koşul sağlanmadığı için atlandı.", Text("step_skipped", null, """{"stepName":"GM"}"""));
        Assert.Equal("Talep onaylandı.", Text("completed"));
        Assert.Equal("Muhasebe kuyruğuna eklendi.", Text("accounting_queued"));
        Assert.Equal("Muhasebe sistemine gönderildi.", Text("accounting_sent"));
        Assert.StartsWith("Muhasebe sistemine gönderilemedi", Text("accounting_failed"));
        Assert.Equal("Ayşe Kaya muhasebede işledi: Logo'ya girildi", Text("accounting_processed", Ayse, """{"note":"Logo'ya girildi"}"""));
        Assert.Equal("Ayşe Kaya muhasebede işledi.", Text("accounting_processed", Ayse));
    }

    [Fact]
    public void Removed_approvers_and_unknown_kinds()
    {
        Assert.Equal("Mehmet Demir çalışma alanından çıkarıldığı için “Yönetici” adımı master admin’e yönlendirildi.",
            Text("approver_removed", null, $$"""{"stepName":"Yönetici","removedId":"{{Mehmet}}","replacements":["{{Ayse}}"]}"""));
        Assert.Equal("Bir onaylayıcı çalışma alanından çıkarıldığı için “Yönetici” adımı kalan onaylayıcılara bırakıldı.",
            Text("approver_removed", null, """{"stepName":"Yönetici"}"""));
        Assert.Equal("something_new", Text("something_new"));
    }
}

public class RequestTextDelegationTests
{
    private static readonly Guid Ayse = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Mehmet = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly IReadOnlyDictionary<Guid, string> People = new Dictionary<Guid, string> { [Ayse] = "Ayşe Kaya", [Mehmet] = "Mehmet Demir" };
    private static string Text(string kind, Guid? actor, string detail) => RequestText.Event(kind, actor, JsonDocument.Parse(detail).RootElement.Clone(), People);

    [Fact]
    public void Decisions_made_by_a_deputy_name_the_person_they_acted_for()
    {
        var detail = $$"""{"stepName":"Yönetici onayı","comment":"Tamam","onBehalfOf":"{{Ayse}}"}""";
        Assert.Equal("Mehmet Demir, Ayşe Kaya adına “Yönetici onayı” adımını onayladı: Tamam", Text("approved", Mehmet, detail));
        Assert.Equal("Mehmet Demir, Ayşe Kaya adına “Yönetici onayı” adımında reddetti: Tamam", Text("rejected", Mehmet, detail));
        Assert.StartsWith("Mehmet Demir, Bir kullanıcı adına", Text("approved", Mehmet, $$"""{"stepName":"x","onBehalfOf":"{{Guid.NewGuid()}}"}"""));
        Assert.Equal("Mehmet Demir “x” adımını onayladı.", Text("approved", Mehmet, """{"stepName":"x"}"""));       // vekâlet yoksa eski cümle
    }

    [Fact]
    public void Reassignment_names_both_people()
    {
        Assert.Equal("Ayşe Kaya “Yönetici onayı” görevinin onaylayıcısını değiştirdi: Mehmet Demir → Ayşe Kaya.",
            Text("reassigned", Ayse, $$"""{"stepName":"Yönetici onayı","from":"{{Mehmet}}","to":"{{Ayse}}"}"""));
    }
}

public class RequestTextLeaveTests
{
    private static string Text(string kind, string detail) => RequestText.Event(kind, null, JsonDocument.Parse(detail).RootElement.Clone(), new Dictionary<Guid, string>());

    [Fact]
    public void Leave_reservation_and_refund_are_explained_in_the_timeline()
    {
        Assert.Equal("5 gün izin 2027 yılı bakiyenizden ayrıldı (kalan: 9 gün).", Text("leave_reserved", """{"days":5,"year":2027,"balanceAfter":9}"""));
        Assert.Equal("3,5 gün izin 2027 yılı bakiyenizden ayrıldı (kalan: 10,5 gün).", Text("leave_reserved", """{"days":3.5,"year":2027,"balanceAfter":10.5}"""));
        Assert.Equal("5 gün izin 2027 yılı bakiyenize iade edildi.", Text("leave_refunded", """{"days":5,"year":2027}"""));
        Assert.Contains("?", Text("leave_reserved", "{}"));                         // eksik ayrıntı çökertmez
    }
}
