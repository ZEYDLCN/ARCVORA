using System.Text.Json;
using Arcvora.Core.Impact;
using Arcvora.Core.Model;
using Arcvora.Core.Patching;
using Arcvora.Core.Samples;
using Arcvora.Core.Validation;

namespace Arcvora.Core.Tests;

public class PatchTests
{
    private static readonly ArchitectureModel Base = ExpenseSample.Model();

    [Fact]
    public void Sample_change_applies_cleanly_and_adds_no_new_blocking_findings()
    {
        var (patch, _) = ExpenseSample.TwoStageApprovalChange();
        var applied = ModelPatcher.Apply(Base, patch);
        Assert.True(applied.Ok, string.Join("; ", applied.Errors));
        Assert.Empty(ModelPatcher.NewBlockingFindings(Base, applied.Model!));
        Assert.Contains(applied.Model!.Requirements, r => r.Id == "REQ-010");
        Assert.Contains(applied.Model.Entities, e => e.Id == "ENT-009");
    }

    [Fact]
    public void Apply_does_not_mutate_the_input_model()
    {
        var (patch, _) = ExpenseSample.TwoStageApprovalChange();
        var before = ModelHasher.Hash(Base);
        ModelPatcher.Apply(Base, patch);
        Assert.Equal(before, ModelHasher.Hash(Base));
    }

    [Fact]
    public void Accepting_confirms_proposals_but_keeps_origin_and_respects_deferred()
    {
        var (patch, _) = ExpenseSample.TwoStageApprovalChange();
        var m = ModelPatcher.Apply(Base, patch, confirmApplied: true).Model!;
        var dec8 = m.Decisions.First(d => d.Id == "DEC-008");
        Assert.Equal(ReviewStatus.Confirmed, dec8.ReviewStatus);
        Assert.Equal(Origin.SystemProposal, dec8.Origin);       // sistem önerisi olduğu bilgisi kaybolmaz
        Assert.Equal(ReviewStatus.Deferred, m.Decisions.First(d => d.Id == "DEC-007").ReviewStatus);
        Assert.Equal(Origin.Assumption, m.Assumptions.First(a => a.Id == "ASM-003").Origin);
    }

    [Fact]
    public void Remove_of_missing_object_is_reported()
    {
        var patch = new ModelPatch { Operations = [ModelPatch.Remove(ObjectType.Component, "CMP-404")] };
        var r = ModelPatcher.Apply(Base, patch);
        Assert.False(r.Ok);
        Assert.Contains("CMP-404", r.Errors[0]);
    }

    [Fact]
    public void Upsert_with_unknown_field_is_rejected_by_strict_parsing()
    {
        var value = JsonSerializer.SerializeToElement(new { id = "CMP-050", name = "X", sneaky = "y" });
        var patch = new ModelPatch { Operations = [new PatchOperation { Op = PatchOpKind.Upsert, Type = ObjectType.Component, Id = "CMP-050", Value = value }] };
        Assert.False(ModelPatcher.Apply(Base, patch).Ok);
    }

    [Fact]
    public void Upsert_with_mismatching_id_is_rejected()
    {
        var value = JsonSerializer.SerializeToElement(new { id = "CMP-051", name = "X" });
        var patch = new ModelPatch { Operations = [new PatchOperation { Op = PatchOpKind.Upsert, Type = ObjectType.Component, Id = "CMP-050", Value = value }] };
        Assert.False(ModelPatcher.Apply(Base, patch).Ok);
    }

    [Fact]
    public void Removing_a_component_without_removing_its_relationships_yields_new_blocking_findings()
    {
        var patch = new ModelPatch { Operations = [ModelPatch.Remove(ObjectType.Component, "CMP-006")] };
        var applied = ModelPatcher.Apply(Base, patch);
        Assert.True(applied.Ok);
        Assert.NotEmpty(ModelPatcher.NewBlockingFindings(Base, applied.Model!));
    }

    [Fact]
    public void Provisional_ids_are_replaced_with_real_ids_and_references_follow()
    {
        var comp = new ArchComponent { Id = "new-1", Name = "Bildirim servisi", Kind = ComponentKind.Module };
        var rel = new Relationship { Id = "new-2", SourceId = "CMP-002", TargetId = "new-1", Description = "olay yayınlar" };
        var req = new Requirement
        {
            Id = "new-3", Text = "Onayda e-posta gitmeli",
            AcceptanceCriteria = [new AcceptanceCriterion { Id = "new-ac", Text = "Onaydan sonra e-posta gönderilir" }],
        };
        var raw = new ModelPatch
        {
            Operations =
            [
                ModelPatch.Upsert(ObjectType.Component, comp),
                ModelPatch.Upsert(ObjectType.Relationship, rel),
                ModelPatch.Upsert(ObjectType.Requirement, req),
            ],
        };

        var resolved = DraftResolver.Resolve(Base, raw);
        var applied = ModelPatcher.Apply(Base, resolved);

        Assert.True(applied.Ok, string.Join("; ", applied.Errors));
        var m = applied.Model!;
        Assert.Contains(m.Components, c => c.Id == "CMP-010" && c.Name == "Bildirim servisi");
        var newRel = m.Relationships.Single(r => r.Description == "olay yayınlar");
        Assert.Equal("CMP-010", newRel.TargetId);
        Assert.Equal("REL-010", newRel.Id);
        Assert.Equal("AC-008", m.Requirements.Single(r => r.Text.StartsWith("Onayda")).AcceptanceCriteria[0].Id);
        Assert.Empty(ModelPatcher.NewBlockingFindings(Base, m));
    }

    [Fact]
    public void Real_ids_are_left_untouched_by_the_resolver()
    {
        var (patch, _) = ExpenseSample.TwoStageApprovalChange();
        var resolved = DraftResolver.Resolve(Base, patch);
        Assert.Equal(patch.Operations.Select(o => o.Id), resolved.Operations.Select(o => o.Id));
    }
}

public class GroupDependencyTests
{
    private static readonly ArchitectureModel Base = ExpenseSample.Model();
    private static readonly ModelPatch Patch = ExpenseSample.TwoStageApprovalChange().Patch;

    [Fact]
    public void Groups_that_reference_objects_created_elsewhere_depend_on_them()
    {
        var deps = GroupDependencies.Compute(Base, Patch);
        Assert.Contains("grp-requirements", deps["grp-data"]);
        Assert.Contains("grp-requirements", deps["grp-api"]);
        Assert.Contains("grp-requirements", deps["grp-flow"]);
        Assert.Contains("grp-requirements", deps["grp-open-question"]);
        Assert.Empty(deps["grp-requirements"]);
    }

    [Fact]
    public void Prose_mentions_of_ids_do_not_create_dependencies()
    {
        // DEC-007 gerekçesi "OQ-004" anıyor ama yapısal bağlantı yok
        var deps = GroupDependencies.Compute(Base, Patch);
        Assert.DoesNotContain("grp-open-question", deps["grp-requirements"]);
    }

    [Fact]
    public void Rejecting_a_group_that_others_depend_on_is_blocked()
    {
        var check = GroupDependencies.CheckSelection(Base, Patch, ["grp-data"]);
        Assert.False(check.Ok);
        Assert.Contains(check.Errors, e => e.Contains("Veri modeli") && e.Contains("Gereksinim ve kararlar"));
    }

    [Fact]
    public void Selecting_a_group_with_all_its_dependencies_is_allowed_and_applies_cleanly()
    {
        var check = GroupDependencies.CheckSelection(Base, Patch, ["grp-requirements", "grp-data"]);
        Assert.True(check.Ok, string.Join("; ", check.Errors));
        var applied = ModelPatcher.Apply(Base, Patch, check.Selected);
        Assert.True(applied.Ok);
        Assert.Empty(ModelPatcher.NewBlockingFindings(Base, applied.Model!));
        Assert.Contains(applied.Model!.Entities, e => e.Id == "ENT-007");
        Assert.DoesNotContain(applied.Model.Endpoints, e => e.Id == "API-006");   // reddedilen grup uygulanmadı
    }

    [Fact]
    public void All_groups_selected_is_the_default()
    {
        var check = GroupDependencies.CheckSelection(Base, Patch, null);
        Assert.True(check.Ok);
        Assert.Equal(5, check.Selected.Count);
    }

    [Fact]
    public void Unknown_or_empty_selection_is_rejected()
    {
        Assert.False(GroupDependencies.CheckSelection(Base, Patch, ["nope"]).Ok);
        Assert.False(GroupDependencies.CheckSelection(Base, Patch, []).Ok);
    }

    [Fact]
    public void Rejecting_a_new_component_but_accepting_the_link_to_it_is_blocked()
    {
        var comp = new ArchComponent { Id = "CMP-020", Name = "Bildirim", Kind = ComponentKind.Module };
        var rel = new Relationship { Id = "REL-020", SourceId = "CMP-002", TargetId = "CMP-020" };
        var patch = new ModelPatch
        {
            Operations = [ModelPatch.Upsert(ObjectType.Component, comp, "g-comp"), ModelPatch.Upsert(ObjectType.Relationship, rel, "g-rel")],
            Groups = [new PatchGroup { Id = "g-comp", Title = "Bileşen" }, new PatchGroup { Id = "g-rel", Title = "Bağlantı" }],
        };
        Assert.False(GroupDependencies.CheckSelection(Base, patch, ["g-rel"]).Ok);
        Assert.True(GroupDependencies.CheckSelection(Base, patch, ["g-comp"]).Ok);
        Assert.True(GroupDependencies.CheckSelection(Base, patch, ["g-comp", "g-rel"]).Ok);
    }

    [Fact]
    public void Removing_a_component_depends_on_the_group_that_removes_its_relationships()
    {
        var patch = new ModelPatch
        {
            Operations =
            [
                ModelPatch.Remove(ObjectType.Component, "CMP-008", "g-cmp"),
                ModelPatch.Remove(ObjectType.Relationship, "REL-008", "g-rel"),
                ModelPatch.Remove(ObjectType.TraceLink, "TRC-022", "g-rel"),
                ModelPatch.Remove(ObjectType.Flow, "FLW-002", "g-flow"),
            ],
            Groups =
            [
                new PatchGroup { Id = "g-cmp", Title = "Bileşeni sil" },
                new PatchGroup { Id = "g-rel", Title = "Bağlantıyı sil" },
                new PatchGroup { Id = "g-flow", Title = "Akışı sil" },
            ],
        };
        var deps = GroupDependencies.Compute(Base, patch);
        Assert.Contains("g-rel", deps["g-cmp"]);
        Assert.Contains("g-flow", deps["g-cmp"]);
        Assert.False(GroupDependencies.CheckSelection(Base, patch, ["g-cmp"]).Ok);
    }
}

public class ImpactTests
{
    private static readonly ArchitectureModel Base = ExpenseSample.Model();

    private static ImpactReport Analyze()
    {
        var (patch, possible) = ExpenseSample.TwoStageApprovalChange();
        var after = ModelPatcher.Apply(Base, patch).Model!;
        return ImpactAnalyzer.Analyze(Base, after, patch, possible);
    }

    [Fact]
    public void Added_and_changed_objects_are_direct_impacts()
    {
        var r = Analyze();
        Assert.Equal(ImpactLevel.Direct, r.Items.Single(i => i.Id == "REQ-010").Level);
        Assert.Equal(ChangeKind.Added, r.Items.Single(i => i.Id == "REQ-010").Change);
        var dec5 = r.Items.Single(i => i.Id == "DEC-005");
        Assert.Equal(ImpactLevel.Direct, dec5.Level);
        Assert.Equal(ChangeKind.Changed, dec5.Change);
        Assert.Equal(ChangeKind.Changed, r.Items.Single(i => i.Id == "ENT-003").Change);
        Assert.Equal(ChangeKind.Changed, r.Items.Single(i => i.Id == "API-003").Change);
        Assert.Equal(ChangeKind.Changed, r.Items.Single(i => i.Id == "FLW-001").Change);
    }

    [Fact]
    public void Unchanged_objects_are_not_reported_as_direct()
    {
        var r = Analyze();
        Assert.DoesNotContain(r.Items, i => i.Id == "REQ-001");
        Assert.DoesNotContain(r.Items, i => i.Id == "CMP-001" && i.Level == ImpactLevel.Direct);
    }

    [Fact]
    public void Dependents_follow_decision_implementers_and_carry_the_reason()
    {
        var r = Analyze();
        // DEC-005 değişti → onu uygulayan API-005 ve CMP-004 bağımlı etki
        var api5 = r.Items.Single(i => i.Id == "API-005");
        Assert.Equal(ImpactLevel.Dependent, api5.Level);
        Assert.Equal("DEC-005", api5.Via);
        Assert.Contains("DEC-005", api5.Reason);
        Assert.Equal(ImpactLevel.Dependent, r.Items.Single(i => i.Id == "CMP-004").Level);
    }

    [Fact]
    public void Reachable_does_not_mean_impacted_hub_components_are_not_expanded()
    {
        var r = Analyze();
        // CMP-004 bağımlı etki; ama onun üzerinden bütün bileşen komşuları (CMP-005 vb.) işaretlenmemeli
        Assert.DoesNotContain(r.Items, i => i.Id == "REL-005" && i.Level == ImpactLevel.Dependent);
        Assert.DoesNotContain(r.Items, i => i.Id == "CMP-006");
        Assert.DoesNotContain(r.Items, i => i.Id == "DEC-001");
    }

    [Fact]
    public void Possible_impacts_come_from_review_and_are_labelled_so()
    {
        var r = Analyze();
        var req4 = r.Items.Single(i => i.Id == "REQ-004");
        Assert.Equal(ImpactLevel.Possible, req4.Level);
        Assert.Contains("Adım bazlı", req4.Reason);
        Assert.Equal(ImpactLevel.Possible, r.Items.Single(i => i.Id == "DEC-002").Level);
        // API-005 hem olası listesinde hem bağımlı: bağımlı (daha güçlü kanıt) kazanır, çift satır oluşmaz
        Assert.Single(r.Items, i => i.Id == "API-005");
    }

    [Fact]
    public void Open_questions_make_impacts_uncertain_and_are_listed()
    {
        var r = Analyze();
        Assert.Contains("OQ-004", r.OpenQuestionIds);
        var ent3 = r.Items.Single(i => i.Id == "ENT-003");
        Assert.Contains("OQ-004", ent3.Blockers);
        Assert.Contains("OQ-002", ent3.Blockers);       // önceden var olan, ertelenmiş soru da görünür
        Assert.Contains("OQ-004", r.Items.Single(i => i.Id == "DEC-007").Blockers);
    }

    [Fact]
    public void Area_summary_groups_counts_by_area()
    {
        var r = Analyze();
        var data = r.Areas.Single(a => a.Area == "Veri modeli");
        Assert.Equal(6, data.Direct);          // ENT-001, 003, 005 değişti + ENT-007, 008, 009 eklendi
    }

    [Fact]
    public void Removing_a_component_marks_its_relationships_flows_and_owned_entities_as_dependents()
    {
        var patch = new ModelPatch { Operations = [ModelPatch.Remove(ObjectType.Component, "CMP-006", "g")] };
        var after = ModelPatcher.Apply(Base, patch).Model!;
        var r = ImpactAnalyzer.Analyze(Base, after, patch);
        Assert.Equal(ChangeKind.Removed, r.Items.Single(i => i.Id == "CMP-006").Change);
        Assert.Equal(ImpactLevel.Dependent, r.Items.Single(i => i.Id == "REL-006").Level);
        Assert.Equal(ImpactLevel.Dependent, r.Items.Single(i => i.Id == "REL-007").Level);
        Assert.Equal(ImpactLevel.Dependent, r.Items.Single(i => i.Id == "FLW-001").Level);
        Assert.Equal(ImpactLevel.Dependent, r.Items.Single(i => i.Id == "ENT-006").Level);
    }

    [Fact]
    public void Empty_patch_has_no_impacts()
    {
        var r = ImpactAnalyzer.Analyze(Base, Base);
        Assert.Empty(r.Items);
    }
}
