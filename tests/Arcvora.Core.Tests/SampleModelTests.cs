using Arcvora.Core.Model;
using Arcvora.Core.Samples;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Tests;

public class SampleModelTests
{
    [Fact]
    public void Sample_has_no_blocking_errors_and_no_warnings_about_structure()
    {
        var report = ModelValidator.Validate(ExpenseSample.Model());
        Assert.False(report.HasBlockingErrors, string.Join("\n", report.Blocking.Select(f => f.RuleId + " " + f.Message)));
        Assert.DoesNotContain(report.Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void Same_model_produces_same_hash_regardless_of_list_order()
    {
        var m = ExpenseSample.Model();
        var shuffled = m with { Requirements = [.. m.Requirements.Reverse()], Components = [.. m.Components.Reverse()] };
        Assert.Equal(ModelHasher.Hash(m), ModelHasher.Hash(shuffled));
    }

    [Fact]
    public void Different_model_produces_different_hash()
    {
        var m = ExpenseSample.Model();
        var changed = m with { Requirements = [.. m.Requirements.Skip(1)] };
        Assert.NotEqual(ModelHasher.Hash(m), ModelHasher.Hash(changed));
    }

    [Fact]
    public void Model_survives_json_roundtrip()
    {
        var m = ExpenseSample.Model();
        var json = ArcvoraJson.Serialize(m);
        var back = ArcvoraJson.Deserialize<ArchitectureModel>(json);
        Assert.Equal(ModelHasher.Hash(m), ModelHasher.Hash(back));
        Assert.Contains("\"origin\":\"user_statement\"", json);
        Assert.Contains("\"reviewStatus\":\"confirmed\"", json);
    }

    [Fact]
    public void Origin_and_review_status_are_independent_fields()
    {
        var req = ExpenseSample.Model().Requirements.First(r => r.Id == "REQ-002");
        Assert.Equal(Origin.SystemProposal, req.Origin);
        Assert.Equal(ReviewStatus.Confirmed, req.ReviewStatus);
    }
}
