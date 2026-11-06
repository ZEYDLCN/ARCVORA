using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

/// <summary>Seçim alanları sabit seçenekler yerine yönetilen bir listeye (ana veri) bağlanabilir.</summary>
public class DataListFieldTests
{
    private static readonly Guid ListId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static FlowDefinition Def(FlowField field) => new()
    {
        Prefix = "LST", SummaryFields = [],
        Fields = [field],
        Steps = [new FlowStep { Id = "s1", Name = "Yönetici", Approvers = new ApproverSpec { Type = ApproverType.Manager } }],
    };

    private static FlowField Select(Guid? list, params string[] options) =>
        Field("dept", FieldType.Select, true) with { ListId = list, Options = options.Length == 0 ? null : options };

    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> Lists(params string[] items) =>
        new Dictionary<Guid, IReadOnlyList<string>> { [ListId] = items };

    private static FormResult Run(FlowField f, string json, IReadOnlyDictionary<Guid, IReadOnlyList<string>>? lists) =>
        FormValidator.Validate(Def(f), Data(json), lists);

    // ------------------------------------------------------------------ tanım

    [Fact]
    public void A_select_may_use_a_list_instead_of_options() =>
        Assert.Empty(FlowValidator.Validate(Def(Select(ListId))));

    [Fact]
    public void A_multi_select_may_use_a_list_too() =>
        Assert.Empty(FlowValidator.Validate(Def(Field("tags", FieldType.MultiSelect) with { ListId = ListId })));

    [Fact]
    public void Options_and_a_list_cannot_be_combined() =>
        Assert.Contains(FlowValidator.Validate(Def(Select(ListId, "A", "B"))), i => i.Code == "options_conflict" && i.Path == "fields[0].options");

    [Fact]
    public void A_select_with_neither_options_nor_list_is_still_invalid() =>
        Assert.Contains(FlowValidator.Validate(Def(Select(null))), i => i.Code == "options_required");

    [Fact]
    public void The_empty_guid_is_not_a_list() =>
        Assert.Contains(FlowValidator.Validate(Def(Select(Guid.Empty))), i => i.Code == "options_required");

    [Theory]
    [InlineData(FieldType.Text)]
    [InlineData(FieldType.Number)]
    [InlineData(FieldType.Date)]
    [InlineData(FieldType.User)]
    [InlineData(FieldType.File)]
    public void Only_select_fields_can_reference_a_list(FieldType type) =>
        Assert.Contains(FlowValidator.Validate(Def(Field("x", type) with { ListId = ListId })), i => i.Code == "invalid_list" && i.Path == "fields[0].listId");

    [Fact]
    public void Conditions_on_list_based_selects_are_allowed()
    {
        var d = Def(Select(ListId)) with
        {
            Steps = [new FlowStep { Id = "s1", Name = "x", Approvers = new ApproverSpec { Type = ApproverType.Manager }, Conditions = [new FlowCondition { Field = "dept", Op = ConditionOp.Eq, Value = Json("\"Satış\"") }] }],
        };
        Assert.Empty(FlowValidator.Validate(d));
    }

    [Fact]
    public void The_list_ids_of_a_definition_can_be_collected()
    {
        var d = new FlowDefinition
        {
            Prefix = "L", Fields = [Select(ListId), Field("a", FieldType.MultiSelect) with { ListId = ListId }, Field("b", FieldType.MultiSelect) with { ListId = Guid.Parse("44444444-4444-4444-4444-444444444444") }, Field("c", FieldType.Text)],
            Steps = [],
        };
        Assert.Equal(2, FormValidator.ListIds(d).Count);
        Assert.Contains(ListId, FormValidator.ListIds(d));
    }

    // ------------------------------------------------------------------ değer

    [Fact]
    public void A_value_from_the_active_items_is_accepted_and_normalised()
    {
        var r = Run(Select(ListId), """{ "dept": "  Satış " }""", Lists("Satış", "Depo"));
        Assert.True(r.IsValid, string.Join(",", r.Issues.Select(i => i.Code)));
        Assert.Equal("Satış", r.Values["dept"]);
    }

    [Fact]
    public void A_value_not_in_the_list_is_refused()
    {
        var r = Run(Select(ListId), """{ "dept": "Finans" }""", Lists("Satış", "Depo"));
        Assert.Contains(r.Issues, i => i.Code == "invalid_option" && i.Path == "data.dept");
    }

    [Fact]
    public void Matching_is_exact_not_case_folded() =>
        Assert.Contains(Run(Select(ListId), """{ "dept": "satış" }""", Lists("Satış")).Issues, i => i.Code == "invalid_option");

    [Fact]
    public void A_missing_list_is_reported_clearly_not_as_a_bad_option()
    {
        var r = Run(Select(ListId), """{ "dept": "Satış" }""", new Dictionary<Guid, IReadOnlyList<string>>());
        Assert.Contains(r.Issues, i => i.Code == "list_missing" && i.Path == "data.dept");
        var none = Run(Select(ListId), """{ "dept": "Satış" }""", null);
        Assert.Contains(none.Issues, i => i.Code == "list_missing");
    }

    [Fact]
    public void An_empty_list_cannot_satisfy_a_required_field()
    {
        var r = Run(Select(ListId), """{ "dept": "x" }""", Lists());
        Assert.False(r.IsValid);
    }

    [Fact]
    public void Multi_select_values_are_checked_against_the_list_and_deduplicated()
    {
        var f = Field("tags", FieldType.MultiSelect) with { ListId = ListId };
        var ok = Run(f, """{ "tags": ["Satış", "Depo", "Satış"] }""", Lists("Satış", "Depo", "Finans"));
        Assert.True(ok.IsValid);
        Assert.Equal(new List<string> { "Satış", "Depo" }, (List<string>)ok.Values["tags"]!);
        Assert.Contains(Run(f, """{ "tags": ["Satış", "Yok"] }""", Lists("Satış")).Issues, i => i.Code == "invalid_option");
    }

    [Fact]
    public void Plain_options_keep_working_without_any_lists()
    {
        var f = Field("kind", FieldType.Select, true) with { Options = ["A", "B"] };
        Assert.True(FormValidator.Validate(Def(f), Data("""{ "kind": "A" }""")).IsValid);
        Assert.True(Run(f, """{ "kind": "B" }""", Lists("zzz")).IsValid);                 // listeler verilse de seçenek alanı etkilenmez
    }
}
