using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Arcvora.Core.Ai;
using Arcvora.Core.Model;

namespace Arcvora.Infrastructure.Ai;

/// <summary>
/// <see cref="ModelDraft"/> için sıkı JSON şeması (enum değerleri, zorunlu alanlar, bilinmeyen alan yok).
/// Yerel modellerde şema kısıtlı çözümleme (llama.cpp grammar, Ollama/vLLM structured output) ile kullanılır;
/// gevşek şema yerine kayıt tiplerinden türetildiği için model ile şema birbirinden sapamaz.
/// </summary>
public static class DraftSchema
{
    private static readonly Lazy<JsonElement> Instance = new(Build);

    public static JsonElement Get() => Instance.Value;

    private static JsonElement Build()
    {
        // Şema üreticisi açık bir TypeInfoResolver ister; sözleşme ayarları (enum adlandırma, sıkı mod) kopyalanır.
        var options = new JsonSerializerOptions(ArcvoraJson.Strict) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var node = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(ModelDraft),
            new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
        return JsonSerializer.SerializeToElement(node);
    }
}
