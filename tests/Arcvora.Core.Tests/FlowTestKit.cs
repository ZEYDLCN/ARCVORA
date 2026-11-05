using System.Text.Json;
using Arcvora.Core.Flows;

namespace Arcvora.Core.Tests;

/// <summary>Akış testleri için ortak tanım ve veri yardımcıları.</summary>
internal static class FlowTestKit
{
    public static readonly Guid Admin1 = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Admin2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    public static readonly Guid Boss = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid Worker = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    public static readonly Guid Gm1 = Guid.Parse("00000000-0000-0000-0000-0000000000d1");
    public static readonly Guid Gm2 = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
    public static readonly Guid GmGroup = Guid.Parse("00000000-0000-0000-0000-0000000000e1");

    public static FlowField Field(string key, FieldType type, bool required = false) => new() { Key = key, Label = key, Type = type, Required = required };

    /// <summary>İzin akışı: tarih aralığı, hesaplanan gün sayısı, tür, vekil, açıklama; yönetici + (10 günden uzunsa) genel müdür grubu.</summary>
    public static FlowDefinition Leave() => new()
    {
        Prefix = "IZN",
        SummaryFields = ["start", "end", "kind"],
        Fields =
        [
            Field("start", FieldType.Date, true),
            Field("end", FieldType.Date, true) with { NotBefore = "start" },
            Field("days", FieldType.Number) with { Computed = new FlowComputed { Op = ComputedOp.CalendarDays, From = "start", To = "end" } },
            Field("kind", FieldType.Select, true) with { Options = ["Yıllık", "Mazeret", "Ücretsiz"] },
            Field("proxy", FieldType.User),
            Field("note", FieldType.LongText) with { MaxLength = 100 },
        ],
        Steps =
        [
            new FlowStep { Id = "s1", Name = "Yönetici onayı", Approvers = new ApproverSpec { Type = ApproverType.Manager } },
            new FlowStep
            {
                Id = "s2", Name = "Genel müdür", Approvers = new ApproverSpec { Type = ApproverType.Group, Id = GmGroup },
                Conditions = [new FlowCondition { Field = "days", Op = ConditionOp.Gt, Value = Json("10") }],
            },
        ],
    };

    public static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    public static Dictionary<string, JsonElement> Data(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    public static Dictionary<string, JsonElement> ValidLeaveData() =>
        Data("""{ "start": "2026-11-10", "end": "2026-11-14", "kind": "Yıllık" }""");

    public sealed class Dir(IReadOnlyList<Guid>? admins = null, params (Guid Group, Guid[] Members)[] groups) : IFlowDirectory
    {
        public IReadOnlyList<Guid> Admins() => admins ?? [Admin1, Admin2];
        public IReadOnlyList<Guid> GroupMembers(Guid groupId) => groups.FirstOrDefault(g => g.Group == groupId).Members ?? [];
    }
}
