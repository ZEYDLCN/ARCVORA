using Arcvora.Core.Flows;
using Arcvora.Infrastructure.Persistence;

namespace Arcvora.Infrastructure.Services;

/// <param name="FieldKey">Talebe bağlanınca hangi ek alanına ait olduğu; hazırlanmış dosyada boş.</param>
public sealed record AttachmentDto(Guid Id, string? FieldKey, string FileName, string ContentType, long Size, string Sha256,
    Guid UploadedBy, DateTimeOffset CreatedAt);

public static class AttachmentList
{
    public static AttachmentDto ToDto(Attachment a) =>
        new(a.Id, a.FieldKey, a.FileName, a.ContentType, a.Size, a.Sha256, a.UploadedBy, a.CreatedAt);

    /// <summary>Talebin eklerini form sırasıyla verir: önce alan sırası, sonra o alandaki dosya sırası (veride tutulan sıra).</summary>
    public static IReadOnlyList<AttachmentDto> Ordered(FlowDefinition def, IReadOnlyDictionary<string, object?> values, IEnumerable<Attachment> attachments)
    {
        var byId = attachments.ToDictionary(a => a.Id);
        var list = new List<AttachmentDto>();
        foreach (var f in def.Fields.Where(f => f.Type == FieldType.File))
            if (values.TryGetValue(f.Key, out var v) && v is IEnumerable<string> ids)
                foreach (var s in ids)
                    if (Guid.TryParse(s, out var id) && byId.TryGetValue(id, out var a)) list.Add(ToDto(a));
        return list;
    }
}
