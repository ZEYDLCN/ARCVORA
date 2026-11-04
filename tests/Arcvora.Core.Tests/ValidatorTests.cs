using Arcvora.Core.Model;
using Arcvora.Core.Samples;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Tests;

public class ValidatorTests
{
    private static ValidationReport Run(ArchitectureModel m) => ModelValidator.Validate(m);

    [Fact]
    public void Dangling_relationship_target_is_a_blocking_reference_error()
    {
        var m = ExpenseSample.Model();
        m = m with { Relationships = [.. m.Relationships, new Relationship { Id = "REL-099", SourceId = "CMP-001", TargetId = "CMP-404" }] };
        var r = Run(m);
        Assert.True(r.HasBlockingErrors);
        Assert.Contains(r.Findings, f => f.RuleId == "REF-001" && f.ObjectId == "REL-099" && f.Kind == FindingKind.Reference);
    }

    [Fact]
    public void Removing_a_component_surfaces_relationships_that_pointed_to_it()
    {
        var m = ExpenseSample.Model();
        m = m with { Components = [.. m.Components.Where(c => c.Id != "CMP-006")] };
        var r = Run(m);
        Assert.Contains(r.Findings, f => f.RuleId == "REF-001" && f.Message.Contains("CMP-006"));
        Assert.Contains(r.Findings, f => f.RuleId == "REF-004" && f.Message.Contains("CMP-006"));
    }

    [Fact]
    public void Duplicate_id_is_schema_error()
    {
        var m = ExpenseSample.Model();
        m = m with { Components = [.. m.Components, m.Components[0] with { Name = "Kopya" }] };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "SCH-001" && f.Kind == FindingKind.Schema);
    }

    [Fact]
    public void Wrong_prefix_is_schema_error()
    {
        var m = ExpenseSample.Model();
        m = m with { Components = [.. m.Components, new ArchComponent { Id = "REQ-777", Name = "Yanlış önek" }] };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "SCH-002" && f.ObjectId == "REQ-777");
    }

    [Fact]
    public void Trace_link_with_incompatible_types_is_rejected()
    {
        var m = ExpenseSample.Model();
        m = m with { TraceLinks = [.. m.TraceLinks, new TraceLink { Id = "TRC-900", FromId = "REQ-001", ToId = "DEC-001", Kind = TraceKind.Addresses }] };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "REF-007" && f.ObjectId == "TRC-900");
    }

    [Fact]
    public void Critical_requirement_without_decision_is_traceability_warning_not_error()
    {
        var m = ExpenseSample.Model();
        m = m with
        {
            Requirements = [.. m.Requirements, new Requirement
            {
                Id = "REQ-050", Text = "Rapor dışa aktarımı olmalı", Kind = RequirementKind.Functional, Priority = Priority.Critical,
            }],
        };
        var r = Run(m);
        Assert.Contains(r.Findings, f => f.RuleId == "TRC-001" && f.ObjectId == "REQ-050" && f.Severity == Severity.Warning);
        Assert.Contains(r.Findings, f => f.RuleId == "TRC-004" && f.ObjectId == "REQ-050");
        Assert.False(r.HasBlockingErrors);
    }

    [Fact]
    public void Vague_quality_requirement_gets_review_suggestion_which_is_not_a_schema_error()
    {
        var m = ExpenseSample.Model();
        m = m with
        {
            Requirements = [.. m.Requirements, new Requirement
            {
                Id = "REQ-051", Text = "Sistem hızlı olsun", Kind = RequirementKind.Quality, Priority = Priority.Medium,
            }],
        };
        var f = Run(m).Findings.Single(x => x.RuleId == "REV-002");
        Assert.Equal(FindingKind.ReviewSuggestion, f.Kind);
        Assert.Equal("REQ-051", f.ObjectId);
        Assert.False(ValidationReport.IsBlocking(f));
    }

    [Fact]
    public void Measurable_quality_requirement_with_criteria_is_not_flagged_as_vague()
    {
        var m = ExpenseSample.Model();
        m = m with
        {
            Requirements = [.. m.Requirements, new Requirement
            {
                Id = "REQ-052", Text = "Sistem hızlı olsun", Kind = RequirementKind.Quality, Priority = Priority.Medium,
                AcceptanceCriteria = [new AcceptanceCriterion { Id = "AC-100", Text = "Liste isteği p95 < 500 ms (1000 eşzamanlı kullanıcı)" }],
            }],
        };
        Assert.DoesNotContain(Run(m).Findings, f => f.RuleId == "REV-002" && f.ObjectId == "REQ-052");
    }

    [Fact]
    public void External_call_without_failure_behavior_is_flagged()
    {
        var m = ExpenseSample.Model();
        m = m with { Relationships = [.. m.Relationships.Select(r => r.Id == "REL-007" ? r with { FailureBehavior = null } : r)] };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "REV-001" && f.ObjectId == "REL-007");
    }

    [Fact]
    public void Profile_conflict_is_reported_as_profile_warning()
    {
        var m = ExpenseSample.Model();
        m = m with { Components = [.. m.Components.Select(c => c.Id == "CMP-002" ? c with { Technology = "Spring Boot + JPA" } : c)] };
        var f = Run(m).Findings.First(x => x.RuleId == "PRF-003");
        Assert.Equal(FindingKind.Profile, f.Kind);
        Assert.Equal("CMP-002", f.ObjectId);
    }

    [Fact]
    public void Unknown_profile_is_an_error_finding()
    {
        var m = ExpenseSample.Model() with { Project = new ProjectInfo { Name = "x", TechProfileId = "cobol" } };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "PRF-001" && f.Severity == Severity.Error);
    }

    [Fact]
    public void Open_question_blocking_a_decision_is_visible_as_info()
    {
        var f = Run(ExpenseSample.Model()).Findings.Where(x => x.RuleId == "TRC-005").ToList();
        Assert.Contains(f, x => x.ObjectId == "DEC-002" && x.Message.Contains("OQ-001"));
        Assert.All(f, x => Assert.Equal(Severity.Info, x.Severity));
    }

    [Fact]
    public void Endpoint_with_invalid_method_or_path_is_schema_error()
    {
        var m = ExpenseSample.Model();
        m = m with { Endpoints = [.. m.Endpoints, new ApiEndpoint { Id = "API-099", Method = "FETCH", Path = "yol" }] };
        var r = Run(m).Findings.Where(f => f.ObjectId == "API-099" && f.RuleId == "SCH-003").ToList();
        Assert.Equal(2, r.Count);
    }

    [Fact]
    public void Flow_step_with_unlisted_participant_is_a_reference_error()
    {
        var m = ExpenseSample.Model();
        var flow = m.Flows.First(f => f.Id == "FLW-002");
        var broken = flow with { Steps = [.. flow.Steps, new FlowStep { From = "CMP-001", To = "CMP-007", Message = "?" }] };
        m = m with { Flows = [.. m.Flows.Select(f => f.Id == flow.Id ? broken : f)] };
        Assert.Contains(Run(m).Findings, f => f.RuleId == "REF-005" && f.ObjectId == "FLW-002");
    }
}
