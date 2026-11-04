using Arcvora.Core.Model;
using Arcvora.Core.Rendering;
using Arcvora.Core.Samples;

namespace Arcvora.Core.Tests;

public class RenderingTests
{
    private static readonly ArchitectureModel Sample = ExpenseSample.Model();

    [Fact]
    public void Component_view_contains_nodes_edges_and_shapes()
    {
        var v = ViewRenderer.Component(Sample).Mermaid;
        Assert.StartsWith("flowchart TD", v);
        Assert.Contains("CMP_005[(\"Uygulama verisi\")]", v);          // veritabanı
        Assert.Contains("CMP_007[[\"Muhasebe servisi\"]]", v);          // dış servis
        Assert.Contains("CMP_006([\"Entegrasyon işleyicisi\"])", v);    // worker
        Assert.Contains("CMP_001 -->|\"HTTPS / JSON\"| CMP_002", v);
    }

    [Fact]
    public void Component_view_does_not_imply_the_database_starts_the_worker()
    {
        var v = ViewRenderer.Component(Sample).Mermaid;
        Assert.Contains("CMP_006 -->|\"Bekleyen gönderimleri okur\"| CMP_005", v);
        Assert.DoesNotContain("CMP_005 --> CMP_006", v);
    }

    [Fact]
    public void Sequence_view_renders_actors_alt_blocks_and_return_arrows()
    {
        var flow = Sample.Flows.First(f => f.Id == "FLW-001");
        var v = ViewRenderer.Sequence(Sample, flow).Mermaid;
        Assert.StartsWith("sequenceDiagram", v);
        Assert.Contains("actor ACT_002 as Yönetici", v);
        Assert.Contains("participant CMP_006 as Entegrasyon işleyicisi", v);
        Assert.Contains("CMP_002-->>CMP_001: 202 Accepted", v);
        Assert.Contains("alt Gönderim başarılı", v);
        Assert.Contains("else Muhasebe hatası", v);
        Assert.Equal(1, v.Split('\n').Count(l => l.Trim() == "end"));
    }

    [Fact]
    public void Er_view_uses_ascii_identifiers_for_attributes_and_cardinality_connectors()
    {
        var v = ViewRenderer.Er(Sample).Mermaid;
        Assert.StartsWith("erDiagram", v);
        Assert.Contains("ENT_003[\"Masraf\"] {", v);
        Assert.Contains("string aciklama", v);        // açıklama → aciklama
        Assert.Contains("uuid kiraci_id", v);          // kiracı_id → kiraci_id
        Assert.Contains("uuid id PK", v);
        Assert.Contains("ENT_001 ||--o{ ENT_002 : \"üyeleri\"", v);
    }

    [Fact]
    public void All_returns_component_er_and_one_sequence_per_flow()
    {
        var views = ViewRenderer.All(Sample);
        Assert.Equal(2 + Sample.Flows.Count, views.Count);
        Assert.Equal(Sample.Flows.Count, views.Count(v => v.Kind == ViewKind.Sequence));
    }

    [Fact]
    public void Empty_model_renders_placeholders_not_broken_diagrams()
    {
        var empty = new ArchitectureModel();
        Assert.Contains("Henüz bileşen yok", ViewRenderer.Component(empty).Mermaid);
        Assert.Contains("Henüz varlık yok", ViewRenderer.Er(empty).Mermaid);
    }

    [Theory]
    [InlineData("Normal ad")]
    [InlineData("Kötü\"; click CMP_001 href \"javascript:alert(1)\"")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("a\nb\nclick X call evil()")]
    [InlineData("%%{init: {'securityLevel':'loose'}}%%")]
    [InlineData("`backtick` ve ; noktalı virgül # diyez")]
    public void Hostile_labels_cannot_inject_directives_or_break_out_of_quotes(string hostile)
    {
        var m = new ArchitectureModel
        {
            Components =
            [
                new ArchComponent { Id = "CMP-001", Name = hostile, Kind = ComponentKind.Api },
                new ArchComponent { Id = "CMP-002", Name = "Diğer" },
            ],
            Relationships = [new Relationship { Id = "REL-001", SourceId = "CMP-001", TargetId = "CMP-002", Description = hostile }],
            Actors = [new Actor { Id = "ACT-001", Name = hostile }],
            Flows =
            [
                new Flow
                {
                    Id = "FLW-001", Name = hostile, Participants = ["ACT-001", "CMP-001"],
                    Steps = [new FlowStep { From = "ACT-001", To = "CMP-001", Message = hostile, AltId = "a", Condition = hostile }],
                },
            ],
        };

        foreach (var view in ViewRenderer.All(m))
        {
            var lines = view.Mermaid.Split('\n');
            // Her satır bilinen bir yapıyla başlar; kullanıcı metni yeni satır başlatamaz.
            foreach (var line in lines.Where(l => l.Length > 0))
            {
                var t = line.TrimStart();
                Assert.False(t.StartsWith("click", StringComparison.OrdinalIgnoreCase), line);
                Assert.False(t.StartsWith("%%", StringComparison.Ordinal), line);
                Assert.DoesNotContain("<script", line, StringComparison.OrdinalIgnoreCase);
            }
            Assert.DoesNotContain('`', view.Mermaid);
            // Mermaid ön işleyicisi %%{init}%% direktiflerini metnin her yerinde işler
            Assert.DoesNotContain("%%", view.Mermaid);
        }

        // Çift tırnak içinde tek bir etiket olarak kalır: tırnak sayısı çifttir.
        var component = ViewRenderer.Component(m).Mermaid;
        foreach (var line in component.Split('\n').Where(l => l.Contains('"')))
            Assert.Equal(0, line.Count(c => c == '"') % 2);
    }

    [Fact]
    public void Labels_that_sanitize_to_nothing_fall_back_to_the_object_id()
    {
        var m = new ArchitectureModel
        {
            Components = [new ArchComponent { Id = "CMP-001", Name = "   ", Kind = ComponentKind.Worker }],
            Entities = [new DataEntity { Id = "ENT-001", Name = "\n\t" }],
        };
        Assert.Contains("CMP_001([\"CMP-001\"])", ViewRenderer.Component(m).Mermaid);
        Assert.Contains("ENT_001[\"ENT-001\"]", ViewRenderer.Er(m).Mermaid);
    }

    [Fact]
    public void Node_ids_are_mermaid_safe()
    {
        Assert.Equal("CMP_001", MermaidText.NodeId("CMP-001"));
        Assert.Equal("n1abc", MermaidText.NodeId("1abc"));
        Assert.Equal("a_b_c", MermaidText.NodeId("a b;c"));
    }

    [Fact]
    public void Handoff_is_deterministic_and_carries_provenance()
    {
        var hash = ModelHasher.Hash(Sample);
        var ctx = new HandoffContext
        {
            VersionNumber = 1, ModelHash = hash,
            History = [new ChangeLogEntry(1, "İlk sürüm", new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero), "ayse@example.com")],
        };
        var a = HandoffRenderer.RenderSingle(Sample, ctx);
        var b = HandoffRenderer.RenderSingle(Sample, ctx);
        Assert.Equal(a, b);
        Assert.Contains($"`{hash[..12]}`", a);
        Assert.Contains(HandoffRenderer.GeneratorVersion, a);
        Assert.Contains("üretime hazır olduğu doğrulandı", a);
        Assert.Contains("```mermaid\nflowchart TD", a);
        Assert.Contains("```mermaid\nsequenceDiagram", a);
        Assert.Contains("```mermaid\nerDiagram", a);
        Assert.Contains("| `REQ-002` | Muhasebe kesintisinde onaylanmış masraf kaybolmamalı |", a);
        Assert.Contains("Sistem önerisi", a);                 // kaynak etiketi
        Assert.Contains("doğrulanmış bir OpenAPI sözleşmesi değildir", a);
        Assert.Contains("OQ-001", a);                          // açık soru görünür
        Assert.Contains("Doğrulanmadı", a);                    // doğrulanmamış varsayım işaretli
    }

    [Fact]
    public void Handoff_neutralizes_html_in_user_text_and_table_pipes()
    {
        var m = ExpenseSample.Model();
        m = m with { Requirements = [.. m.Requirements, new Requirement { Id = "REQ-060", Text = "<img src=x onerror=alert(1)> a|b" }] };
        var md = HandoffRenderer.RenderSingle(m, new HandoffContext { VersionNumber = 1, ModelHash = "abc" });
        Assert.DoesNotContain("<img", md);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt; a\\|b", md);
    }

    [Fact]
    public void Package_contains_the_documented_files()
    {
        var files = HandoffRenderer.RenderPackage(Sample, new HandoffContext { VersionNumber = 1, ModelHash = "abc" });
        Assert.Equal(
            ["PROJECT_OVERVIEW.md", "REQUIREMENTS.md", "ARCHITECTURE.md", "DECISIONS.md", "OPEN_QUESTIONS.md", "CHANGELOG.md"],
            files.Select(f => f.Path).ToArray());
    }
}
