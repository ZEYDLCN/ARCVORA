using System.Text;
using Arcvora.Core.Model;

namespace Arcvora.Core.Rendering;

public enum ViewKind { Component, Sequence, Er }

/// <param name="Mermaid">Mermaid kaynak metni.</param>
public sealed record RenderedView(ViewKind Kind, string? FlowId, string Title, string Mermaid);

/// <summary>
/// Görünümler ortak modelden türetilir; Mermaid bir kalıcı kayıt değil, bir çıktıdır (§1, §14.3).
/// </summary>
public static class ViewRenderer
{
    private static string NonEmpty(string label, string fallback) => label.Length == 0 ? fallback : label;

    public static IReadOnlyList<RenderedView> All(ArchitectureModel m)
    {
        var views = new List<RenderedView> { Component(m), Er(m) };
        views.AddRange(m.Flows.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => Sequence(m, f)));
        return views;
    }

    public static RenderedView Component(ArchitectureModel m)
    {
        var sb = new StringBuilder("flowchart TD\n");
        var live = m.Components.Where(c => c.ReviewStatus != ReviewStatus.Rejected).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (live.Count == 0)
        {
            sb.Append("    EMPTY[\"Henüz bileşen yok\"]\n");
            return new RenderedView(ViewKind.Component, null, "Bileşen görünümü", sb.ToString());
        }

        var ids = live.Select(c => c.Id).ToHashSet();
        foreach (var c in live)
        {
            var id = MermaidText.NodeId(c.Id);
            var label = NonEmpty(MermaidText.Label(c.Name), c.Id);   // boş etiket Mermaid'de sözdizimi hatasıdır
            var node = c.Kind switch
            {
                ComponentKind.Database or ComponentKind.Storage => $"{id}[(\"{label}\")]",
                ComponentKind.ExternalService => $"{id}[[\"{label}\"]]",
                ComponentKind.Queue or ComponentKind.Worker => $"{id}([\"{label}\"])",
                _ => $"{id}[\"{label}\"]",
            };
            sb.Append("    ").Append(node).Append('\n');
        }

        foreach (var r in m.Relationships.Where(r => ids.Contains(r.SourceId) && ids.Contains(r.TargetId)
                                                     && r.ReviewStatus != ReviewStatus.Rejected)
                     .OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var arrow = r.Communication is CommunicationKind.Async or CommunicationKind.Event ? "-.->" : "-->";
            var label = MermaidText.Label(r.Description);
            var edge = label.Length == 0 ? arrow : $"{arrow}|\"{label}\"|";
            sb.Append("    ").Append(MermaidText.NodeId(r.SourceId)).Append(' ').Append(edge).Append(' ')
              .Append(MermaidText.NodeId(r.TargetId)).Append('\n');
        }

        return new RenderedView(ViewKind.Component, null, "Bileşen görünümü", sb.ToString());
    }

    public static RenderedView Sequence(ArchitectureModel m, Flow flow)
    {
        var index = new ModelIndex(m);
        var sb = new StringBuilder("sequenceDiagram\n");

        foreach (var p in flow.Participants.Distinct())
        {
            var found = index.Find(p);
            if (found is null) continue;
            var keyword = found.Type == ObjectType.Actor ? "actor" : "participant";
            sb.Append("    ").Append(keyword).Append(' ').Append(MermaidText.NodeId(p))
              .Append(" as ").Append(MermaidText.Seq(found.Title)).Append('\n');
        }

        string? openAlt = null;
        string? lastCondition = null;
        var participants = flow.Participants.ToHashSet();

        foreach (var s in flow.Steps.Where(s => participants.Contains(s.From) && participants.Contains(s.To)))
        {
            if (s.AltId is null)
            {
                if (openAlt is not null) { sb.Append("    end\n"); openAlt = null; lastCondition = null; }
            }
            else if (openAlt != s.AltId)
            {
                if (openAlt is not null) sb.Append("    end\n");
                sb.Append("    alt ").Append(MermaidText.Seq(s.Condition)).Append('\n');
                openAlt = s.AltId;
                lastCondition = s.Condition;
            }
            else if (s.Condition != lastCondition)
            {
                sb.Append("    else ").Append(MermaidText.Seq(s.Condition)).Append('\n');
                lastCondition = s.Condition;
            }

            var arrow = s.Kind switch { StepKind.Async => "-)", StepKind.Return => "-->>", _ => "->>" };
            var indent = openAlt is null ? "    " : "        ";
            sb.Append(indent).Append(MermaidText.NodeId(s.From)).Append(arrow).Append(MermaidText.NodeId(s.To))
              .Append(": ").Append(MermaidText.Seq(s.Message)).Append('\n');
        }
        if (openAlt is not null) sb.Append("    end\n");

        return new RenderedView(ViewKind.Sequence, flow.Id, $"Akış: {flow.Name}", sb.ToString());
    }

    public static RenderedView Er(ArchitectureModel m)
    {
        var sb = new StringBuilder("erDiagram\n");
        var live = m.Entities.Where(e => e.ReviewStatus != ReviewStatus.Rejected).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (live.Count == 0)
        {
            sb.Append("    EMPTY[\"Henüz varlık yok\"]\n");
            return new RenderedView(ViewKind.Er, null, "Veri modeli", sb.ToString());
        }

        var ids = live.Select(e => e.Id).ToHashSet();
        foreach (var e in live)
        {
            var id = MermaidText.NodeId(e.Id);
            sb.Append("    ").Append(id).Append("[\"").Append(NonEmpty(MermaidText.Label(e.Name), e.Id)).Append("\"]");
            if (e.Fields.Count == 0) { sb.Append('\n'); continue; }
            sb.Append(" {\n");
            foreach (var f in e.Fields)
            {
                sb.Append("        ").Append(MermaidText.Identifier(f.Type, "string")).Append(' ')
                  .Append(MermaidText.Identifier(f.Name));
                if (f.IsKey) sb.Append(" PK");
                if (!string.IsNullOrWhiteSpace(f.Notes)) sb.Append(" \"").Append(MermaidText.Label(f.Notes)).Append('"');
                sb.Append('\n');
            }
            sb.Append("    }\n");
        }

        foreach (var e in live)
        foreach (var rel in e.Relations.Where(r => ids.Contains(r.TargetEntityId)))
        {
            var connector = rel.Cardinality switch
            {
                Cardinality.OneToOne => "||--||",
                Cardinality.OneToMany => "||--o{",
                Cardinality.ManyToOne => "}o--||",
                Cardinality.ManyToMany => "}o--o{",
                Cardinality.ZeroOrOneToMany => "|o--o{",
                _ => "||--o{",
            };
            var label = MermaidText.Label(string.IsNullOrWhiteSpace(rel.Label) ? "ilişki" : rel.Label);
            sb.Append("    ").Append(MermaidText.NodeId(e.Id)).Append(' ').Append(connector).Append(' ')
              .Append(MermaidText.NodeId(rel.TargetEntityId)).Append(" : \"").Append(label).Append("\"\n");
        }

        return new RenderedView(ViewKind.Er, null, "Veri modeli", sb.ToString());
    }
}
