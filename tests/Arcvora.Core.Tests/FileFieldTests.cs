using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

/// <summary>Ek (fiş/belge/tarama) alanı: tanım doğrulaması ve form değerinin normalleştirilmesi.</summary>
public class FileFieldTests
{
    private static readonly Guid F1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid F2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static FlowDefinition WithFile(int? maxFiles = 2, bool required = false) => new()
    {
        Prefix = "MSF",
        SummaryFields = ["amount"],
        Fields = [Field("amount", FieldType.Money, true), Field("receipt", FieldType.File, required) with { MaxFiles = maxFiles }],
        Steps = [new FlowStep { Id = "s1", Name = "Yönetici", Approvers = new ApproverSpec { Type = ApproverType.Manager } }],
    };

    private static FormResult Run(string json, FlowDefinition? def = null) => FormValidator.Validate(def ?? WithFile(), Data(json));

    // ---------------------------------------------------------------- tanım

    [Fact]
    public void A_file_field_with_sane_limits_is_a_valid_definition() =>
        Assert.Empty(FlowValidator.Validate(WithFile()));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void Max_files_must_be_between_1_and_10(int max) =>
        Assert.Contains(FlowValidator.Validate(WithFile(max)), i => i.Code == "invalid_range" && i.Path == "fields[1].maxFiles");

    [Fact]
    public void Max_files_is_only_allowed_on_file_fields()
    {
        var d = WithFile();
        d = d with { Fields = [d.Fields[0] with { MaxFiles = 2 }, d.Fields[1]] };
        Assert.Contains(FlowValidator.Validate(d), i => i.Code == "invalid_range" && i.Path == "fields[0].maxFiles");
    }

    [Fact]
    public void A_file_field_cannot_drive_a_condition()
    {
        var d = WithFile() with
        {
            Steps = [new FlowStep
            {
                Id = "s1", Name = "x", Approvers = new ApproverSpec { Type = ApproverType.Manager },
                Conditions = [new FlowCondition { Field = "receipt", Op = ConditionOp.Eq, Value = Json("\"a\"") }],
            }],
        };
        Assert.Contains(FlowValidator.Validate(d), i => i.Code == "invalid_condition");
    }

    // ----------------------------------------------------------------- değer

    [Fact]
    public void File_ids_are_kept_in_order_and_deduplicated()
    {
        var r = Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F2}}", "{{F1}}"] }""");
        Assert.True(r.IsValid, string.Join("; ", r.Issues.Select(i => i.Code)));
        Assert.Equal(new List<string> { F1.ToString(), F2.ToString() }, (List<string>)r.Values["receipt"]!);
    }

    [Fact]
    public void An_optional_file_field_can_be_left_empty()
    {
        Assert.True(Run("""{ "amount": 10 }""").IsValid);
        var empty = Run("""{ "amount": 10, "receipt": [] }""");
        Assert.True(empty.IsValid);
        Assert.False(empty.Values.ContainsKey("receipt"));
    }

    [Fact]
    public void A_required_file_field_needs_at_least_one_file()
    {
        var d = WithFile(required: true);
        Assert.Contains(Run("""{ "amount": 10 }""", d).Issues, i => i.Code == "required" && i.Path == "data.receipt");
        Assert.Contains(Run("""{ "amount": 10, "receipt": [] }""", d).Issues, i => i.Code == "required" && i.Path == "data.receipt");
    }

    [Fact]
    public void Too_many_files_are_rejected_after_deduplication()
    {
        var three = Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F2}}", "{{Guid.NewGuid()}}"] }""");
        Assert.Contains(three.Issues, i => i.Code == "too_many_files" && i.Path == "data.receipt");
        // Aynı dosyayı iki kez göndermek sınırı aşmaz.
        Assert.True(Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F1}}", "{{F2}}"] }""").IsValid);
    }

    [Theory]
    [InlineData("\"not-an-array\"")]
    [InlineData("42")]
    [InlineData("{}")]
    public void Non_array_values_are_rejected(string value) =>
        Assert.Contains(Run($$"""{ "amount": 10, "receipt": {{value}} }""").Issues, i => i.Code == "invalid_type" && i.Path == "data.receipt");

    [Theory]
    [InlineData("\"nope\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("\"00000000-0000-0000-0000-000000000000\"")]
    public void Array_items_must_be_real_file_ids(string item) =>
        Assert.Contains(Run($$"""{ "amount": 10, "receipt": [{{item}}] }""").Issues, i => i.Code == "invalid_file" && i.Path == "data.receipt");

    [Fact]
    public void Default_max_files_is_three_when_unset()
    {
        var d = WithFile(maxFiles: null);
        Assert.True(Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F2}}", "{{Guid.NewGuid()}}"] }""", d).IsValid);
        Assert.Contains(Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F2}}", "{{Guid.NewGuid()}}", "{{Guid.NewGuid()}}"] }""", d).Issues, i => i.Code == "too_many_files");
    }

    [Fact]
    public void File_ids_of_a_form_can_be_listed_for_ownership_checks()
    {
        var r = Run($$"""{ "amount": 10, "receipt": ["{{F1}}", "{{F2}}"] }""");
        var ids = FormValidator.FileFieldIds(WithFile(), r.Values);
        Assert.Equal(2, ids.Count);
        Assert.All(ids, x => Assert.Equal("receipt", x.Key));
    }

    [Fact]
    public void File_fields_do_not_appear_in_the_summary()
    {
        var d = WithFile() with { SummaryFields = ["amount", "receipt"] };
        var r = Run($$"""{ "amount": 10, "receipt": ["{{F1}}"] }""", d);
        Assert.Equal("10.00 TRY", FlowValues.Summary(d, r.Values));
    }
}
