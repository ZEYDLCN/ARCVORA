using Arcvora.Core.Flows;
using static Arcvora.Core.Tests.FlowTestKit;

namespace Arcvora.Core.Tests;

public class FlowValidatorTests
{
    private static IReadOnlyList<FlowIssue> Run(FlowDefinition d) => FlowValidator.Validate(d);
    private static void Has(FlowDefinition d, string code) => Assert.Contains(Run(d), i => i.Code == code);

    [Fact]
    public void A_well_formed_leave_definition_has_no_issues() => Assert.Empty(Run(Leave()));

    [Fact]
    public void Duplicate_field_keys_are_rejected() =>
        Has(Leave() with { Fields = [.. Leave().Fields, Field("start", FieldType.Text)] }, "duplicate_key");

    [Theory]
    [InlineData("Bad Key")]
    [InlineData("1abc")]
    [InlineData("")]
    [InlineData("çalışan")]
    public void Field_keys_must_be_lowercase_identifiers(string key) =>
        Has(Leave() with { Fields = [Field(key, FieldType.Text)] }, "invalid_key");

    [Fact]
    public void Select_fields_need_unique_non_empty_options()
    {
        var f = Field("kind", FieldType.Select);
        Has(Leave() with { Fields = [f], SummaryFields = [] }, "options_required");
        Has(Leave() with { Fields = [f with { Options = ["A", "A"] }], SummaryFields = [] }, "duplicate_option");
        Has(Leave() with { Fields = [f with { Options = ["A", " "] }], SummaryFields = [] }, "options_required");
    }

    [Fact]
    public void NotBefore_must_point_to_another_date_field()
    {
        var d = Leave();
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "end" ? f with { NotBefore = "end" } : f)] }, "invalid_reference");
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "end" ? f with { NotBefore = "kind" } : f)] }, "invalid_reference");
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "end" ? f with { NotBefore = "yok" } : f)] }, "invalid_reference");
    }

    [Fact]
    public void Computed_fields_must_be_numbers_over_date_fields_and_never_required()
    {
        var d = Leave();
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "days" ? f with { Computed = f.Computed! with { From = "kind" } } : f)] }, "invalid_computed");
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "days" ? f with { Type = FieldType.Text } : f)] }, "invalid_computed");
        Has(d with { Fields = [.. d.Fields.Select(f => f.Key == "days" ? f with { Required = true } : f)] }, "computed_required");
    }

    [Fact]
    public void Range_limits_must_be_consistent()
    {
        var f = Field("amount", FieldType.Money) with { Min = 100, Max = 10 };
        Has(Leave() with { Fields = [f], SummaryFields = [] }, "invalid_range");
        Has(Leave() with { Fields = [Field("t", FieldType.Text) with { MaxLength = 0 }], SummaryFields = [] }, "invalid_range");
        Has(Leave() with { Fields = [Field("t", FieldType.Text) with { MaxLength = 100000 }], SummaryFields = [] }, "invalid_range");
    }

    [Fact]
    public void A_flow_needs_between_one_and_ten_steps()
    {
        Has(Leave() with { Steps = [] }, "steps_required");
        var step = Leave().Steps[0];
        Has(Leave() with { Steps = [.. Enumerable.Range(0, 11).Select(i => step with { Id = $"s{i}" })] }, "too_many_steps");
    }

    [Fact]
    public void Step_ids_and_names_are_validated()
    {
        var s = Leave().Steps[0];
        Has(Leave() with { Steps = [s, s] }, "duplicate_step_id");
        Has(Leave() with { Steps = [s with { Id = "Bad Id" }] }, "invalid_key");
        Has(Leave() with { Steps = [s with { Name = " " }] }, "invalid_name");
    }

    [Fact]
    public void User_and_group_approvers_need_an_id_and_manager_or_admin_must_not_have_one()
    {
        var s = Leave().Steps[0];
        Has(Leave() with { Steps = [s with { Approvers = new ApproverSpec { Type = ApproverType.User } }] }, "approver_id_required");
        Has(Leave() with { Steps = [s with { Approvers = new ApproverSpec { Type = ApproverType.Group } }] }, "approver_id_required");
        Has(Leave() with { Steps = [s with { Approvers = new ApproverSpec { Type = ApproverType.Manager, Id = Boss } }] }, "approver_id_unexpected");
        Assert.Empty(Run(Leave() with { Steps = [s with { Approvers = new ApproverSpec { Type = ApproverType.Admin } }] }));
    }

    [Fact]
    public void Conditions_must_reference_known_fields_with_compatible_operators_and_values()
    {
        var s = Leave().Steps[0];
        FlowDefinition With(FlowCondition c) => Leave() with { Steps = [s with { Conditions = [c] }] };

        Has(With(new FlowCondition { Field = "yok", Op = ConditionOp.Eq, Value = Json("1") }), "unknown_field");
        Has(With(new FlowCondition { Field = "kind", Op = ConditionOp.Gt, Value = Json("\"A\"") }), "invalid_condition");
        Has(With(new FlowCondition { Field = "days", Op = ConditionOp.Gt, Value = Json("\"on\"") }), "condition_value");
        Has(With(new FlowCondition { Field = "kind", Op = ConditionOp.In, Value = Json("\"Yıllık\"") }), "condition_value");
        Has(With(new FlowCondition { Field = "days", Op = ConditionOp.Eq }), "condition_value");
        Has(With(new FlowCondition { Field = "note", Op = ConditionOp.Eq, Value = Json("\"x\"") }), "invalid_condition");
        Assert.Empty(Run(With(new FlowCondition { Field = "kind", Op = ConditionOp.In, Value = Json("[\"Yıllık\",\"Mazeret\"]") })));
        Assert.Empty(Run(With(new FlowCondition { Field = "start", Op = ConditionOp.Gte, Value = Json("\"2026-01-01\"") })));
    }

    [Fact]
    public void Summary_fields_must_exist_and_are_limited_to_three()
    {
        Has(Leave() with { SummaryFields = ["yok"] }, "unknown_field");
        Has(Leave() with { SummaryFields = ["start", "end", "kind", "proxy"] }, "too_many_summary_fields");
    }

    [Theory]
    [InlineData("")]
    [InlineData("izn")]
    [InlineData("TOOLONGPREFIX")]
    [InlineData("A-B")]
    public void Prefix_must_be_short_uppercase_alphanumeric(string prefix) => Has(Leave() with { Prefix = prefix }, "invalid_prefix");

    [Fact]
    public void Accounting_needs_a_category_when_enabled()
    {
        Has(Leave() with { Completion = new FlowCompletion { Accounting = new AccountingSpec { Enabled = true } } }, "category_required");
        Assert.Empty(Run(Leave() with { Completion = new FlowCompletion { Accounting = new AccountingSpec { Enabled = true, Category = "izin" } } }));
        Assert.Empty(Run(Leave() with { Completion = new FlowCompletion { Accounting = new AccountingSpec { Enabled = false } } }));
    }

    [Fact]
    public void A_flow_needs_at_least_one_field_and_at_most_sixty()
    {
        Has(Leave() with { Fields = [], SummaryFields = [], Steps = [Leave().Steps[0]] }, "fields_required");
        Has(Leave() with { Fields = [.. Enumerable.Range(0, 61).Select(i => Field($"f{i}", FieldType.Text))], SummaryFields = [] }, "too_many_fields");
    }

    [Fact]
    public void Unsupported_schema_is_rejected() => Has(Leave() with { Schema = 2 }, "unsupported_schema");
}
