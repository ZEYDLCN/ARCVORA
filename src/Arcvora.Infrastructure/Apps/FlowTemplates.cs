using System.Text.Json;
using Arcvora.Core.Flows;
using Arcvora.Core.Model;

namespace Arcvora.Infrastructure.Apps;

public sealed record FlowTemplate(string Id, string Name, string Description, string Icon, FlowDefinition Definition);

/// <summary>Hazır akış şablonları (izin, avans, masraf). Arayüzdeki şablon listesiyle aynı dosyadan gelir.</summary>
public static class FlowTemplates
{
    private static readonly Lazy<IReadOnlyList<FlowTemplate>> Cache = new(Load);

    public static IReadOnlyList<FlowTemplate> All => Cache.Value;

    public static FlowTemplate Get(string id) => All.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"Şablon yok: {id}");

    private static IReadOnlyList<FlowTemplate> Load()
    {
        using var s = typeof(FlowTemplates).Assembly.GetManifestResourceStream("templates.json") ?? throw new InvalidOperationException("Gömülü şablon dosyası bulunamadı.");
        using var doc = JsonDocument.Parse(s);
        var list = new List<FlowTemplate>();
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            var def = t.GetProperty("definition").Deserialize<FlowDefinition>(ArcvoraJson.Options) ?? throw new InvalidOperationException("Şablon tanımı okunamadı.");
            list.Add(new FlowTemplate(t.GetProperty("id").GetString()!, t.GetProperty("name").GetString()!,
                t.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "", t.TryGetProperty("icon", out var i) ? i.GetString() ?? "file" : "file", def));
        }
        return list;
    }
}
