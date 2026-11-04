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

    [Fact]
    public void ScanForIds_finds_ids_mentioned_inside_free_text()
    {
        var found = Ids.ScanForIds("CMP-002, OQ-004 yanıtlanmadan DEC-001 kesinleşmez; (REQ-010) ve AC-001 de etkilenir.").ToList();
        Assert.Equal(["CMP-002", "OQ-004", "DEC-001", "REQ-010", "AC-001"], found);
    }

    [Theory]
    [InlineData("kimlik yok")]
    [InlineData("XCMP-001")]          // sözcük sınırı: başka bir sözcüğün parçası değil
    [InlineData("CMP-01")]
    public void ScanForIds_ignores_look_alikes(string text) => Assert.Empty(Ids.ScanForIds(text));

    [Fact]
    public void StructuralRefs_returns_only_fields_whose_whole_value_is_an_object_id()
    {
        const string json = """
        {
          "id": "REL-001", "sourceId": "CMP-001", "targetId": "CMP-002",
          "description": "CMP-009 hakkında serbest metin anması",
          "criteria": [ { "id": "AC-001", "text": "x" } ],
          "participants": ["ACT-001", "CMP-003"],
          "nested": { "deep": { "ref": "DEC-007" } },
          "number": 5, "flag": true, "none": null
        }
        """;
        var refs = Ids.StructuralRefs(json);
        Assert.Equal(["REL-001", "CMP-001", "CMP-002", "ACT-001", "CMP-003", "DEC-007"], refs);
        Assert.DoesNotContain("CMP-009", refs);   // serbest metin anması yapısal başvuru değildir
        Assert.DoesNotContain("AC-001", refs);    // iç içe kabul ölçütü kimlikleri sayılmaz
    }

    [Fact]
    public void StructuralRefs_rejects_invalid_json() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Ids.StructuralRefs("{bozuk"));

    [Fact]
    public void Next_is_one_more_than_the_highest_number_and_never_reuses_gaps()
    {
        Assert.Equal("REQ-001", Ids.Next("REQ", []));
        Assert.Equal("REQ-004", Ids.Next("REQ", ["REQ-001", "REQ-003"]));         // REQ-002 boşluğu yeniden kullanılmaz
        Assert.Equal("REQ-011", Ids.Next("REQ", ["REQ-010", "REQ-002"]));
    }

    [Fact]
    public void Next_only_looks_at_the_requested_prefix_and_ignores_malformed_ids()
    {
        Assert.Equal("CMP-001", Ids.Next("CMP", ["REQ-009", "DEC-004"]));
        Assert.Equal("CMP-003", Ids.Next("CMP", ["CMP-002", "cmp-099", "new-7", "CMP-x"]));
    }

    [Fact]
    public void Next_pads_to_three_digits_and_grows_past_999()
    {
        Assert.Equal("REQ-1000", Ids.Next("REQ", ["REQ-999"]));
        Assert.Equal("REQ-010", Ids.Next("REQ", ["REQ-009"]));
    }
}

public class ProfileCatalogTests
{
    [Theory]
    [InlineData("aspnetcore")]
    [InlineData("ASPNETCORE")]
    [InlineData("AspNetCore")]
    public void Find_is_case_insensitive(string id) => Assert.Same(ProfileCatalog.AspNetCore, ProfileCatalog.Find(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yok-boyle-profil")]
    public void Find_returns_null_for_unknown_or_missing_ids(string? id) => Assert.Null(ProfileCatalog.Find(id));

    [Fact]
    public void Profile_ids_are_unique_and_every_listed_profile_is_findable()
    {
        Assert.Equal(ProfileCatalog.All.Count, ProfileCatalog.All.Select(p => p.Id.ToLowerInvariant()).Distinct().Count());
        Assert.All(ProfileCatalog.All, p => Assert.Same(p, ProfileCatalog.Find(p.Id)));
    }
}
