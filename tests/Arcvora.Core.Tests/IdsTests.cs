using Arcvora.Core.Model;
using Arcvora.Core.Profiles;

namespace Arcvora.Core.Tests;

public class IdsTests
{
    [Theory]
    [InlineData("REQ-001", true)]
    [InlineData("CMP-1000", true)]          // 3+ hane: 1000'den sonra da biçim geçerli
    [InlineData("AC-001", true)]
    [InlineData("req-001", false)]          // büyük harf zorunlu
    [InlineData("REQ-01", false)]           // en az 3 hane
    [InlineData("REQ001", false)]
    [InlineData("new-1", false)]            // sağlayıcı-geçici kimlik
    [InlineData("REQ-001 ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWellFormed_accepts_only_PREFIX_NNN(string? id, bool expected) =>
        Assert.Equal(expected, Ids.IsWellFormed(id));

    [Theory]
    [InlineData("SRC-001", ObjectType.Source)]
    [InlineData("REQ-002", ObjectType.Requirement)]
    [InlineData("ASM-003", ObjectType.Assumption)]
    [InlineData("DEC-004", ObjectType.Decision)]
    [InlineData("CMP-005", ObjectType.Component)]
    [InlineData("REL-006", ObjectType.Relationship)]
    [InlineData("ENT-007", ObjectType.Entity)]
    [InlineData("ACT-008", ObjectType.Actor)]
    [InlineData("FLW-009", ObjectType.Flow)]
    [InlineData("API-010", ObjectType.Endpoint)]
    [InlineData("TRC-011", ObjectType.TraceLink)]
    [InlineData("OQ-012", ObjectType.OpenQuestion)]
    public void TypeOf_maps_every_prefix_to_its_object_type(string id, ObjectType expected) =>
        Assert.Equal(expected, Ids.TypeOf(id));

    [Theory]
    [InlineData("AC-001")]      // kabul ölçütü bir nesne türü değildir
    [InlineData("XYZ-001")]
    [InlineData("new-1")]
    [InlineData(null)]
    public void TypeOf_is_null_for_unknown_prefixes_and_malformed_ids(string? id) => Assert.Null(Ids.TypeOf(id));

    [Fact]
    public void Prefix_and_TypeOf_round_trip_for_every_object_type()
    {
        foreach (var t in Enum.GetValues<ObjectType>())
        {
            var prefix = Ids.Prefix(t);
            Assert.Equal(t, Ids.TypeOf($"{prefix}-001"));
        }
    }

    [Fact]
    public void IsAcceptanceCriterionId_recognises_only_AC()
    {
        Assert.True(Ids.IsAcceptanceCriterionId("AC-003"));
        Assert.False(Ids.IsAcceptanceCriterionId("REQ-003"));
        Assert.False(Ids.IsAcceptanceCriterionId("ac-003"));
        Assert.False(Ids.IsAcceptanceCriterionId(null));
    }
}
