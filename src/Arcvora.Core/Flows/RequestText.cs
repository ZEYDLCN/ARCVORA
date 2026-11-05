using System.Text.Json;

namespace Arcvora.Core.Flows;

/// <summary>İskelet (henüz uygulanmadı).</summary>
public static class RequestText
{
    public static string StatusLabel(string status) => "";
    public static string Event(string kind, Guid? actorId, JsonElement? detail, IReadOnlyDictionary<Guid, string> people) => "";
}
