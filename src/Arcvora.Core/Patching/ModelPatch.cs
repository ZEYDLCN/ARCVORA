using System.Text.Json;
using Arcvora.Core.Model;

namespace Arcvora.Core.Patching;

public enum PatchOpKind { Upsert, Remove }

public sealed record PatchOperation
{
    public required PatchOpKind Op { get; init; }
    public required ObjectType Type { get; init; }
    public required string Id { get; init; }
    /// <summary>Upsert için nesnenin tamamı (ilgili modelin JSON hâli).</summary>
    public JsonElement? Value { get; init; }
    public string? GroupId { get; init; }
}

/// <summary>Kısmi kabulde birlikte kabul/ret edilen değişiklik grubu.</summary>
public sealed record PatchGroup
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
}

public sealed record ModelPatch
{
    public const string MiscGroupId = "grp-misc";

    public string Summary { get; init; } = "";
    public IReadOnlyList<PatchOperation> Operations { get; init; } = [];
    public IReadOnlyList<PatchGroup> Groups { get; init; } = [];

    public bool IsEmpty => Operations.Count == 0;

    /// <summary>Grubu olmayan işlemleri "Diğer değişiklikler" grubuna toplar; eksik grup kayıtlarını tamamlar.</summary>
    public ModelPatch WithDefaultGroups()
    {
        var groups = Groups.ToList();
        var ops = new List<PatchOperation>(Operations.Count);
        foreach (var op in Operations)
        {
            var gid = op.GroupId;
            if (string.IsNullOrWhiteSpace(gid)) gid = MiscGroupId;
            if (groups.All(g => g.Id != gid))
                groups.Add(new PatchGroup { Id = gid, Title = gid == MiscGroupId ? "Diğer değişiklikler" : gid });
            ops.Add(op with { GroupId = gid });
        }
        return this with { Operations = ops, Groups = groups.Where(g => ops.Any(o => o.GroupId == g.Id)).ToList() };
    }

    public static PatchOperation Upsert<T>(ObjectType type, T value, string? groupId = null) where T : IModelObject =>
        new()
        {
            Op = PatchOpKind.Upsert,
            Type = type,
            Id = value.Id,
            Value = JsonSerializer.SerializeToElement(value, ArcvoraJson.Options),
            GroupId = groupId,
        };

    public static PatchOperation Remove(ObjectType type, string id, string? groupId = null) =>
        new() { Op = PatchOpKind.Remove, Type = type, Id = id, GroupId = groupId };
}
