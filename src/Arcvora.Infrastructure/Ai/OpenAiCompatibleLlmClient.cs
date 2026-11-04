using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Arcvora.Core.Ai;
using Arcvora.Infrastructure.Common;
using Microsoft.Extensions.Options;

namespace Arcvora.Infrastructure.Ai;

/// <summary>
/// OpenAI uyumlu yerel sunucular için istemci (Ollama, llama.cpp `llama-server`, vLLM, LM Studio).
/// Araç çağrısı yerine <c>response_format: json_schema</c> kullanılır: yerel modellerde zorunlu tool_choice
/// güvenilmezdir, şema kısıtlı çözümleme ise geçerli JSON'u sunucu tarafında garanti eder.
/// Yine de çıktı güvenilmeyen girdi sayılır; doğrulama ve onarım boru hattındadır.
/// </summary>
public sealed partial class OpenAiCompatibleLlmClient(HttpClient http, IOptions<AiOptions> options) : ILlmClient
{
    public string ProviderName => $"local:{options.Value.Local.Model}";

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^```(?:json)?\s*|\s*```$", RegexOptions.IgnoreCase)]
    private static partial Regex CodeFence();

    public async Task<StructuredResponse> GenerateAsync(StructuredRequest request, CancellationToken ct)
    {
        var o = options.Value.Local;
        var body = new JsonObject
        {
            ["model"] = o.Model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = o.DisableThinking ? request.User + "\n/no_think" : request.User }),
            ["max_tokens"] = Math.Min(request.MaxOutputTokens, o.MaxOutputTokens),
            ["temperature"] = o.Temperature,
            ["stream"] = false,
            ["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = request.ToolName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(request.InputSchema.GetRawText()),
                },
            },
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(o.BaseUrl.TrimEnd('/') + "/"), "v1/chat/completions"))
        {
            // StringContent: Content-Length ile gider (JsonContent chunked gönderir; bazı yerel sunucu/proxy'ler bunu desteklemez).
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(o.ApiKey)) msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        HttpResponseMessage res;
        try
        {
            res = await http.SendAsync(msg, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmTransientException($"Yerel model sunucusuna ulaşılamadı ({o.BaseUrl}): {ex.Message}. Sunucu çalışıyor mu?", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LlmTransientException("Yerel model yanıtı zaman aşımına uğradı; modeli küçültün veya Ai__TimeoutSeconds değerini artırın.", ex);
        }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw Classify(res.StatusCode, text);

            JsonDocument doc;
            try { doc = JsonDocument.Parse(text); }
            catch (JsonException ex) { throw new LlmTransientException("Yerel sunucu geçersiz yanıt döndürdü.", ex); }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                    throw new LlmException("Yerel sunucu yanıtında seçenek yok.");

                var choice = choices[0];
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.GetString() == "length")
                    throw new LlmException("Model çıktısı token sınırında kesildi. Bağlam/çıktı sınırını (num_ctx, -c, Ai__Local__MaxOutputTokens) artırın veya daha büyük bir model kullanın.");

                var content = choice.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                var json = CleanJson(content);
                if (json.Length == 0) throw new LlmException("Model boş çıktı döndürdü.");

                var (inTok, outTok) = Usage(root, request, json);
                return new StructuredResponse(json, inTok, outTok, root.TryGetProperty("model", out var mo) ? mo.GetString() ?? o.Model : o.Model);
            }
        }
    }

    /// <summary>`&lt;think&gt;` bloklarını ve kod çitlerini temizler; yine de JSON değilse ilk '{' – son '}' aralığını alır.</summary>
    public static string CleanJson(string content)
    {
        var s = ThinkBlock().Replace(content, "").Trim();
        s = CodeFence().Replace(s, "").Trim();
        if (s.Length > 0 && s[0] != '{')
        {
            var a = s.IndexOf('{');
            var b = s.LastIndexOf('}');
            if (a >= 0 && b > a) s = s[a..(b + 1)];
        }
        return s;
    }

    private static (int In, int Out) Usage(JsonElement root, StructuredRequest req, string output)
    {
        if (root.TryGetProperty("usage", out var u))
        {
            var i = u.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pi) ? pi : 0;
            var o = u.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
            if (i + o > 0) return (i, o);
        }
        // Bazı yerel sunucular kullanım bilgisi vermez; bütçe yine işlesin diye kabaca (≈4 karakter/token) tahmin edilir.
        return ((req.System.Length + req.User.Length) / 4, output.Length / 4);
    }

    private static Exception Classify(HttpStatusCode status, string body)
    {
        var detail = "";
        try
        {
            using var d = JsonDocument.Parse(body);
            if (d.RootElement.TryGetProperty("error", out var e))
                detail = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        }
        catch (JsonException) { detail = body.Length > 200 ? body[..200] : body; }
        if (detail.Length > 300) detail = detail[..300];

        var code = (int)status;
        if (status == HttpStatusCode.TooManyRequests || code >= 500)   // sunucu meşgul / model yükleniyor
            return new LlmTransientException($"Yerel model sunucusu geçici olarak kullanılamıyor ({code}). {detail}".Trim());
        return new LlmException($"Yerel model sunucusu isteği reddetti ({code}). {detail}".Trim());
    }
}
