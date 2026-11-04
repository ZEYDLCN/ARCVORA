using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Ai;

/// <summary>
/// Anthropic Messages API istemcisi. Şemalı çıktı için araç çağrısı zorunlu kılınır (tool_choice).
/// API anahtarı yalnızca istek başlığında gider; log veya hata mesajına yazılmaz.
/// </summary>
public sealed class AnthropicLlmClient(HttpClient http, IOptions<AiOptions> options) : ILlmClient
{
    public string ProviderName => "anthropic";

    public async Task<StructuredResponse> GenerateAsync(StructuredRequest request, CancellationToken ct)
    {
        var o = options.Value.Anthropic;
        var body = new JsonObject
        {
            ["model"] = o.Model,
            ["max_tokens"] = request.MaxOutputTokens,
            ["system"] = request.System,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = request.User }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = request.ToolName,
                ["description"] = request.ToolDescription,
                ["input_schema"] = JsonNode.Parse(request.InputSchema.GetRawText()),
            }),
            ["tool_choice"] = new JsonObject { ["type"] = "tool", ["name"] = request.ToolName },
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), "v1/messages"))
        {
            Content = JsonContent.Create(body),
        };
        msg.Headers.Add("x-api-key", o.ApiKey);
        msg.Headers.Add("anthropic-version", o.Version);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(msg, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmTransientException("Sağlayıcıya ulaşılamadı.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmTransientException("Sağlayıcı isteği zaman aşımına uğradı.", ex);
        }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw Classify(res.StatusCode, text);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch (JsonException ex) { throw new LlmTransientException("Sağlayıcı geçersiz yanıt döndürdü.", ex); }

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("stop_reason", out var sr) && sr.GetString() == "max_tokens")
                    throw new LlmException("Sağlayıcı çıktısı token sınırında kesildi; görevi küçültün.");

                if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    throw new LlmException("Sağlayıcı yanıtında içerik yok.");

                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var t) && t.GetString() == "tool_use"
                        && block.TryGetProperty("name", out var n) && n.GetString() == request.ToolName
                        && block.TryGetProperty("input", out var input))
                    {
                        var usage = root.TryGetProperty("usage", out var u) ? u : default;
                        return new StructuredResponse(input.GetRawText(), Tokens(usage, "input_tokens"), Tokens(usage, "output_tokens"),
                            root.TryGetProperty("model", out var m) ? m.GetString() ?? o.Model : o.Model);
                    }
                }
                throw new LlmException("Sağlayıcı beklenen araç çağrısını döndürmedi.");
            }
        }
    }

    private static int Tokens(JsonElement usage, string name) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : 0;

    private static Exception Classify(HttpStatusCode status, string body)
    {
        var detail = "";
        try
        {
            using var d = JsonDocument.Parse(body);
            if (d.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                detail = m.GetString() ?? "";
        }
        catch (JsonException) { }
        if (detail.Length > 300) detail = detail[..300];

        var code = (int)status;
        if (status == HttpStatusCode.TooManyRequests || code >= 500)   // 429, 5xx, 529 (overloaded)
            return new LlmTransientException($"Sağlayıcı geçici olarak kullanılamıyor ({code}).");
        return new LlmException($"Sağlayıcı isteği reddetti ({code}). {detail}".Trim());
    }
}
