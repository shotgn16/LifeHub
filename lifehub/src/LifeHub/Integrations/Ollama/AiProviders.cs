using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeHub.Integrations.Ollama;

/// <summary>A model running inside the app on this device (registered only by the phone app).</summary>
public interface IOnDeviceAi
{
    bool Available { get; }
    IReadOnlyList<string> Models { get; }
    /// <summary>Runs one chat on the device; returns the answer text. Images aren't supported.</summary>
    Task<string> ChatAsync(string model, string system, string user, bool json, CancellationToken ct);
}

/// <summary>
/// The request and response formats of the cloud AI services. Every provider gets the same system prompt and
/// user text; for JSON answers the schema is added to the instructions (and passed natively where the API
/// supports it), and the answer is parsed leniently by <see cref="AiClient"/>.
/// </summary>
public static class AiProviders
{
    public static string Label(AiProvider p) => p switch
    {
        AiProvider.OpenAI => "ChatGPT (OpenAI)",
        AiProvider.Gemini => "Google Gemini",
        AiProvider.Claude => "Claude (Anthropic)",
        AiProvider.OpenAICompatible => "OpenAI-compatible server",
        AiProvider.OnDevice => "On this device",
        _ => "Ollama"
    };

    public static bool IsCloud(AiProvider p) => p is AiProvider.OpenAI or AiProvider.Gemini or AiProvider.Claude;

    /// <summary>Good starting models (the List button shows everything your key can use).</summary>
    public static (string Model, string Vision) Defaults(AiProvider p) => p switch
    {
        AiProvider.OpenAI => ("gpt-5-mini", "gpt-5-mini"),
        AiProvider.Gemini => ("gemini-2.5-flash", "gemini-2.5-flash"),
        AiProvider.Claude => ("claude-haiku-5-5", "claude-haiku-5-5"),
        AiProvider.OnDevice => ("Qwen3-1.7B-q4f16_1-MLC", ""),
        _ => ("qwen2.5:7b", "")
    };

    public static string KeyUrl(AiProvider p) => p switch
    {
        AiProvider.OpenAI => "https://platform.openai.com/api-keys",
        AiProvider.Gemini => "https://aistudio.google.com/apikey",
        AiProvider.Claude => "https://console.anthropic.com/settings/keys",
        _ => ""
    };

    public static string SchemaHint(JsonObject schema) =>
        "\n\nAnswer with ONLY a JSON object (no markdown, no explanation) that matches this JSON schema:\n" + schema.ToJsonString();

    private static string Mime(byte[] b) => b.Length > 3 && b[0] == 0x89 && b[1] == 0x50 ? "image/png" : b.Length > 3 && b[0] == 0x52 && b[1] == 0x49 ? "image/webp" : "image/jpeg";

    // ---------------------------------------------------------------- OpenAI and compatible
    public static HttpRequestMessage OpenAI(string baseUrl, string key, string model, string system, string user, JsonObject? schema,
        IReadOnlyList<byte[]>? images, double temperature, int maxTokens, bool strictSchema)
    {
        JsonNode content = user;
        if (images is { Count: > 0 })
        {
            var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = user } };
            foreach (var i in images)
                parts.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{Mime(i)};base64,{Convert.ToBase64String(i)}" } });
            content = parts;
        }
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system + (schema is null ? "" : SchemaHint(schema)) },
                new JsonObject { ["role"] = "user", ["content"] = content }
            }
        };
        // OpenAI's newer models only accept the default temperature and use max_completion_tokens.
        if (strictSchema) body["max_completion_tokens"] = maxTokens;
        else { body["max_tokens"] = maxTokens; body["temperature"] = temperature; }
        if (schema is not null)
            body["response_format"] = strictSchema
                ? new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = "answer", ["schema"] = schema.DeepClone(), ["strict"] = false } }
                : new JsonObject { ["type"] = "json_object" };
        var req = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/chat/completions") { Content = JsonHttp.Body(body) };
        if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        return req;
    }

    public static string OpenAIText(JsonElement j) =>
        j.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

    // ---------------------------------------------------------------- Gemini
    public static HttpRequestMessage Gemini(string key, string model, string system, string user, JsonObject? schema,
        IReadOnlyList<byte[]>? images, double temperature, int maxTokens)
    {
        var parts = new JsonArray { new JsonObject { ["text"] = user } };
        foreach (var i in images ?? Array.Empty<byte[]>())
            parts.Add(new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = Mime(i), ["data"] = Convert.ToBase64String(i) } });
        var config = new JsonObject { ["temperature"] = temperature, ["maxOutputTokens"] = maxTokens };
        if (schema is not null) config["responseMimeType"] = "application/json";
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system + (schema is null ? "" : SchemaHint(schema)) } } },
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = parts } },
            ["generationConfig"] = config
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        { Content = JsonHttp.Body(body) };
        req.Headers.Add("x-goog-api-key", key.Trim());
        return req;
    }

    public static string GeminiText(JsonElement j)
    {
        if (!j.TryGetProperty("candidates", out var c) || c.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini returned no answer" + (j.TryGetProperty("promptFeedback", out var f) ? $" ({f})" : ""));
        return string.Concat(c[0].GetProperty("content").GetProperty("parts").EnumerateArray()
            .Where(p => p.TryGetProperty("text", out _) && !(p.TryGetProperty("thought", out var t) && t.GetBoolean()))
            .Select(p => p.GetProperty("text").GetString()));
    }

    // ---------------------------------------------------------------- Claude
    public static HttpRequestMessage Claude(string key, string model, string system, string user, JsonObject? schema,
        IReadOnlyList<byte[]>? images, double temperature, int maxTokens)
    {
        var content = new JsonArray();
        foreach (var i in images ?? Array.Empty<byte[]>())
            content.Add(new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = Mime(i), ["data"] = Convert.ToBase64String(i) } });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = user });
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["temperature"] = temperature,
            ["system"] = system + (schema is null ? "" : SchemaHint(schema)),
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content } }
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = JsonHttp.Body(body) };
        req.Headers.Add("x-api-key", key.Trim());
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    public static string ClaudeText(JsonElement j) =>
        string.Concat(j.GetProperty("content").EnumerateArray().Where(b => b.GetProperty("type").GetString() == "text").Select(b => b.GetProperty("text").GetString()));

    // ---------------------------------------------------------------- model lists
    public static HttpRequestMessage ListModels(AiProvider p, string key, string baseUrl)
    {
        HttpRequestMessage req;
        switch (p)
        {
            case AiProvider.Gemini:
                req = new(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
                req.Headers.Add("x-goog-api-key", key.Trim());
                break;
            case AiProvider.Claude:
                req = new(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=100");
                req.Headers.Add("x-api-key", key.Trim());
                req.Headers.Add("anthropic-version", "2023-06-01");
                break;
            default:
                req = new(HttpMethod.Get, (p == AiProvider.OpenAI ? "https://api.openai.com/v1" : baseUrl.TrimEnd('/')) + "/models");
                if (!string.IsNullOrWhiteSpace(key)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
                break;
        }
        return req;
    }

    public static List<string> ParseModels(AiProvider p, JsonElement j)
    {
        if (p == AiProvider.Gemini)
            return j.GetProperty("models").EnumerateArray()
                .Where(m => m.TryGetProperty("supportedGenerationMethods", out var g) && g.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                .Select(m => m.GetProperty("name").GetString()!.Replace("models/", "")).OrderBy(x => x).ToList();
        var ids = j.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!);
        if (p == AiProvider.OpenAI) ids = ids.Where(i => i.StartsWith("gpt") || i.StartsWith("o") || i.StartsWith("chatgpt"));
        return ids.OrderBy(x => x).ToList();
    }
}
