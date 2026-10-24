using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeHub.Integrations.Ollama;

/// <summary>Talks to a local Ollama server (https://ollama.com) — nothing leaves your network.</summary>
public sealed class OllamaClient
{
    private readonly IHttpClientFactory _http;
    private readonly SettingsService _settings;

    public OllamaClient(IHttpClientFactory http, SettingsService settings)
    {
        _http = http; _settings = settings;
    }

    public bool IsConfigured => _settings.Current.Ollama is { Enabled: true } o && !string.IsNullOrWhiteSpace(o.Url) && !string.IsNullOrWhiteSpace(o.Model);

    private HttpClient Client()
    {
        var o = _settings.Current.Ollama;
        var c = _http.CreateClient("ollama");
        c.BaseAddress = new Uri(o.Url.TrimEnd('/') + "/");
        c.Timeout = TimeSpan.FromSeconds(Math.Max(10, o.TimeoutSeconds));
        return c;
    }

    public async Task<List<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var r = await Client().GetAsync("api/tags", ct);
        r.EnsureSuccessStatusCode();
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return j.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()!).OrderBy(x => x).ToList();
    }

    /// <summary>
    /// Chat completion constrained to a JSON schema (Ollama structured outputs, v0.5+).
    /// Returns the parsed JSON object. Pass images (JPEG/PNG bytes) to use the vision model.
    /// </summary>
    public async Task<JsonElement> ChatJsonAsync(string system, string user, JsonObject schema, CancellationToken ct = default,
        IReadOnlyList<byte[]>? images = null, string? model = null)
    {
        var content = await ChatAsync(system, user, schema, images, model, ct);
        return ParseLenient(content);
    }

    /// <summary>Plain-text chat (e.g. transcribing handwriting).</summary>
    public Task<string> ChatTextAsync(string system, string user, CancellationToken ct = default,
        IReadOnlyList<byte[]>? images = null, string? model = null) =>
        ChatAsync(system, user, null, images, model, ct);

    public bool HasVision => IsConfigured && !string.IsNullOrWhiteSpace(_settings.Current.Ollama.VisionModel);

    /// <summary>The model to use with images: the vision model if set, otherwise the main model.</summary>
    public string VisionModel
    {
        get { var o = _settings.Current.Ollama; return string.IsNullOrWhiteSpace(o.VisionModel) ? o.Model : o.VisionModel; }
    }

    private async Task<string> ChatAsync(string system, string user, JsonObject? schema, IReadOnlyList<byte[]>? images, string? model, CancellationToken ct)
    {
        var o = _settings.Current.Ollama;
        JsonObject Build(bool sendThink)
        {
            var userMsg = new JsonObject { ["role"] = "user", ["content"] = user };
            if (images is { Count: > 0 })
                userMsg["images"] = new JsonArray(images.Select(i => (JsonNode)JsonValue.Create(Convert.ToBase64String(i))!).ToArray());
            var request = new JsonObject
            {
                ["model"] = model ?? (images is { Count: > 0 } ? VisionModel : o.Model),
                ["stream"] = false,
                ["options"] = new JsonObject
                {
                    ["temperature"] = o.Temperature,
                    ["num_ctx"] = Math.Clamp(o.ContextTokens, 2048, 131072),
                    ["num_predict"] = Math.Clamp(o.MaxAnswerTokens, 256, 16384)
                },
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = "system", ["content"] = system },
                    userMsg
                }
            };
            // Thinking models (qwen3, qwen3-vl…) can otherwise "reason" for minutes before answering.
            if (sendThink) request["think"] = o.Thinking;
            if (schema is not null) request["format"] = schema.DeepClone();
            return request;
        }

        var (status, body) = await PostChatAsync(Build(sendThink: true), ct);
        // Older Ollama versions or models without a thinking mode may reject the "think" option.
        if (status == 400 && body.Contains("think", StringComparison.OrdinalIgnoreCase))
            (status, body) = await PostChatAsync(Build(sendThink: false), ct);
        if (status is < 200 or >= 300) throw new InvalidOperationException($"Ollama returned {status}: {body[..Math.Min(300, body.Length)]}");
        var content = JsonDocument.Parse(body).RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        // Some model templates leak their reasoning into the answer.
        return Regex.Replace(content, @"<think>[\s\S]*?</think>", "").Trim();
    }

    private async Task<(int Status, string Body)> PostChatAsync(JsonObject request, CancellationToken ct)
    {
        try
        {
            using var r = await Client().PostAsync("api/chat", JsonHttp.Body(request), ct);
            return ((int)r.StatusCode, await r.Content.ReadAsStringAsync(ct));
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            var o = _settings.Current.Ollama;
            throw new TimeoutException($"Ollama didn't answer within {o.TimeoutSeconds} seconds. The first request after a while loads the model, which can take a minute — " +
                "try again, raise the timeout in Settings → AI, or use a smaller model.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Can't reach Ollama at {_settings.Current.Ollama.Url}: {ex.Message}. Check it's running and allows network connections (OLLAMA_HOST / \"Expose Ollama to the network\").");
        }
    }

    public bool HasEmbeddings => IsConfigured && !string.IsNullOrWhiteSpace(_settings.Current.Ollama.EmbeddingModel);

    /// <summary>Vector for semantic similarity (Ollama /api/embed). Null when no embedding model is set.</summary>
    public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (!HasEmbeddings) return null;
        var request = new JsonObject { ["model"] = _settings.Current.Ollama.EmbeddingModel, ["input"] = text.Length > 8000 ? text[..8000] : text };
        using var r = await Client().PostAsync("api/embed", JsonHttp.Body(request), ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama embed returned {(int)r.StatusCode}: {body[..Math.Min(200, body.Length)]}");
        var root = JsonDocument.Parse(body).RootElement;
        if (!root.TryGetProperty("embeddings", out var e) || e.GetArrayLength() == 0) return null;
        return e[0].EnumerateArray().Select(x => (float)x.GetDouble()).ToArray();
    }

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Accepts plain JSON or JSON wrapped in ``` fences / extra prose (older models).</summary>
    public static JsonElement ParseLenient(string content)
    {
        content = content.Trim();
        try { return JsonDocument.Parse(content).RootElement.Clone(); }
        catch (JsonException)
        {
            var start = content.IndexOf('{');
            var end = content.LastIndexOf('}');
            if (start >= 0 && end > start) return JsonDocument.Parse(content[start..(end + 1)]).RootElement.Clone();
            throw new InvalidOperationException("The model did not return JSON: " + content[..Math.Min(200, content.Length)]);
        }
    }
}
