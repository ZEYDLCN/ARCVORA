using System.Text.Json;
using System.Text.Json.Nodes;
using Arcvora.Core.Model;

namespace Arcvora.Core.Patching;

/// <summary>
/// LLM/heuristic çıktısındaki sağlayıcı-geçici kimlikleri ("new-1" gibi) gerçek kimliklere çevirir.
/// Üreticinin gerçek kimlik yazmasına güvenilmez; kimlik çakışması riskini sistem üstlenir.
/// </summary>
public static class DraftResolver
{
    public static ModelPatch Resolve(ArchitectureModel baseModel, ModelPatch raw)
    {
        var index = new ModelIndex(baseModel);
        var used = new HashSet<string>(index.All.Select(o => o.Id), StringComparer.Ordinal);
        foreach (var ac in baseModel.Requirements.SelectMany(r => r.AcceptanceCriteria)) used.Add(ac.Id);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        // 1) Nesne kimlikleri
        foreach (var op in raw.Operations.Where(o => o.Op == PatchOpKind.Upsert && !Ids.IsWellFormed(o.Id)))
        {
            if (map.ContainsKey(op.Id)) continue;
            map[op.Id] = Allocate(Ids.Prefix(op.Type), used);
        }

        // 2) Kabul ölçütü kimlikleri (iç içe)
        foreach (var op in raw.Operations.Where(o => o.Value is { ValueKind: JsonValueKind.Object }))
        {
            if (op.Type != ObjectType.Requirement) continue;
            var node = JsonNode.Parse(op.Value!.Value.GetRawText());
            if (node?["acceptanceCriteria"] is not JsonArray arr) continue;
            foreach (var item in arr.OfType<JsonObject>())
            {
                var id = item["id"]?.GetValue<string>();
                if (id is null) continue;
                if (!Ids.IsWellFormed(id) && !map.ContainsKey(id))
                    map[id] = Allocate(Ids.AcceptancePrefix, used);
            }
        }

        if (map.Count == 0) return raw.WithDefaultGroups();

        var ops = raw.Operations.Select(op =>
        {
            var newId = map.GetValueOrDefault(op.Id, op.Id);
            JsonElement? value = null;
            if (op.Value is { } v)
            {
                var node = JsonNode.Parse(v.GetRawText());
                Rewrite(node, map);
                value = JsonSerializer.SerializeToElement(node, ArcvoraJson.Options);
            }
            return op with { Id = newId, Value = value };
        }).ToList();

        return (raw with { Operations = ops }).WithDefaultGroups();
    }

    private static string Allocate(string prefix, HashSet<string> used)
    {
        var id = Ids.Next(prefix, used);
        used.Add(id);
        return id;
    }

    private static void Rewrite(JsonNode? node, Dictionary<string, string> map)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (obj[key] is JsonValue jv && jv.TryGetValue<string>(out var s) && map.TryGetValue(s, out var repl))
                        obj[key] = repl;
                    else
                        Rewrite(obj[key], map);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue jv && jv.TryGetValue<string>(out var s) && map.TryGetValue(s, out var repl))
                        arr[i] = repl;
                    else
                        Rewrite(arr[i], map);
                }
                break;
        }
    }
}
