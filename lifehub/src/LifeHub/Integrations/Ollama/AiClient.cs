using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeHub.Integrations.Ollama;

/// <summary>
/// LifeHub's AI: Ollama on your own computer or network (private), ChatGPT, Gemini, Claude, any OpenAI-compatible
/// server, or a model inside the phone app (Settings → AI). Everything else in LifeHub just calls
/// <see cref="ChatJsonAsync"/> / <see cref="ChatTextAsync"/> and doesn't care which one answers.
/// </summary>
public sealed class AiClient
{
    private readonly IHttpClientFactory _http;
    private readonly SettingsService _settings;
    private readonly IOnDeviceAi? _onDevice;

    public AiClient(IHttpClientFactory http, SettingsService settings, IEnumerable<IOnDeviceAi> onDevice)
    {
        _http = http; _settings = settings; _onDevice = onDevice.FirstOrDefault();
    }

    public AiProvider Provider => _settings.Current.Ollama.Provider;
    public string ProviderLabel => AiProviders.Label(Provider);
    /// <summary>The on-device AI is chosen but the app isn't open to run it (phone in the background): wait.</summary>
    public bool WaitingForDevice => _settings.Current.Ollama is { Enabled: true, Provider: AiProvider.OnDevice } && _onDevice is { Available: false };

    /// <summary>A model that runs inside this app is possible here (the phone app).</summary>
    public bool OnDeviceSupported => _onDevice is not null;

    public bool IsConfigured => _settings.Current.Ollama is { Enabled: true } o && !string.IsNullOrWhiteSpace(o.Model) && o.Provider switch
    {
        AiProvider.Ollama => !string.IsNullOrWhiteSpace(o.Url),
        AiProvider.OpenAICompatible => !string.IsNullOrWhiteSpace(o.BaseUrl),
        AiProvider.OnDevice => _onDevice?.Available == true,
        _ => !string.IsNullOrWhiteSpace(o.ApiKey)
    };

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
        var o = _settings.Current.Ollama;
        if (o.Provider == AiProvider.OnDevice) return _onDevice?.Models.ToList() ?? new();
        if (o.Provider != AiProvider.Ollama)
        {
            using var lr = await Cloud().SendAsync(AiProviders.ListModels(o.Provider, o.ApiKey, o.BaseUrl), ct);
            var lb = await lr.Content.ReadAsStringAsync(ct);
            if (!lr.IsSuccessStatusCode) throw new InvalidOperationException($"{ProviderLabel} returned {(int)lr.StatusCode}: {Short(lb)}");
            return AiProviders.ParseModels(o.Provider, JsonDocument.Parse(lb).RootElement);
        }
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

    /// <summary>Photos can be read: a vision model is set for Ollama, and the cloud services' models see images anyway.</summary>
    public bool HasVision => IsConfigured && (AiProviders.IsCloud(Provider) || !string.IsNullOrWhiteSpace(_settings.Current.Ollama.VisionModel));

    /// <summary>The model to use with images: the vision model if set, otherwise the main model.</summary>
    public string VisionModel
    {
        get { var o = _settings.Current.Ollama; return string.IsNullOrWhiteSpace(o.VisionModel) ? o.Model : o.VisionModel; }
    }

    private HttpClient Cloud()
    {
        var c = _http.CreateClient("ai");
        c.Timeout = TimeSpan.FromSeconds(Math.Max(10, _settings.Current.Ollama.TimeoutSeconds));
        return c;
    }

    private static string Short(string body) => body[..Math.Min(300, body.Length)];

    private async Task<string> ChatAsync(string system, string user, JsonObject? schema, IReadOnlyList<byte[]>? images, string? model, CancellationToken ct)
    {
        var o = _settings.Current.Ollama;
        if (o.Provider != AiProvider.Ollama)
        {
            var m = model ?? (images is { Count: > 0 } ? VisionModel : o.Model);
            var temp = o.Temperature;
            var max = Math.Clamp(o.MaxAnswerTokens, 256, 16384);
            if (o.Provider == AiProvider.OnDevice)
            {
                if (_onDevice is not { Available: true }) throw new InvalidOperationException("The on-device AI isn't available here. Choose another AI in Settings → AI.");
                var text = await _onDevice.ChatAsync(m, system + (schema is null ? "" : AiProviders.SchemaHint(schema)), user, schema is not null, ct);
                return Regex.Replace(text, @"<think>[\s\S]*?</think>", "").Trim();
            }
            using var req = o.Provider switch
            {
                AiProvider.OpenAI => AiProviders.OpenAI("https://api.openai.com/v1", o.ApiKey, m, system, user, schema, images, temp, max, strictSchema: true),
                AiProvider.OpenAICompatible => AiProviders.OpenAI(o.BaseUrl, o.ApiKey, m, system, user, schema, images, temp, max, strictSchema: false),
                AiProvider.Gemini => AiProviders.Gemini(o.ApiKey, m, system, user, schema, images, temp, max),
                _ => AiProviders.Claude(o.ApiKey, m, system, user, schema, images, temp, max)
            };
            string cbody;
            int cstatus;
            try
            {
                using var res = await Cloud().SendAsync(req, ct);
                cstatus = (int)res.StatusCode;
                cbody = await res.Content.ReadAsStringAsync(ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"{ProviderLabel} didn't answer within {o.TimeoutSeconds} seconds.");
            }
            catch (HttpRequestException ex) { throw new InvalidOperationException($"Can't reach {ProviderLabel}: {ex.Message}"); }
            if (cstatus == 401 || cstatus == 403) throw new InvalidOperationException($"{ProviderLabel} didn't accept the API key (Settings → AI).");
            if (cstatus == 429) throw new InvalidOperationException($"{ProviderLabel} says you've reached your limit for now (rate limit or out of credit).");
            if (cstatus is < 200 or >= 300) throw new InvalidOperationException($"{ProviderLabel} returned {cstatus}: {Short(cbody)}");
            var j = JsonDocument.Parse(cbody).RootElement;
            var answer = o.Provider switch
            {
                AiProvider.Gemini => AiProviders.GeminiText(j),
                AiProvider.Claude => AiProviders.ClaudeText(j),
                _ => AiProviders.OpenAIText(j)
            };
            return Regex.Replace(answer, @"<think>[\s\S]*?</think>", "").Trim();
        }
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
        catch (JsonException) { }
        // Wrapped in ``` fences or prose.
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            try { return JsonDocument.Parse(content[start..(end + 1)]).RootElement.Clone(); }
            catch (JsonException) { }
        }
        // Cut off part-way (ran out of room): keep every complete item.
        if (Salvage(content) is { } repaired)
        {
            try { return JsonDocument.Parse(repaired).RootElement.Clone(); }
            catch (JsonException) { }
        }
        throw new AiFormatException("The AI's answer was cut off or wasn't in the expected format. Please try again — " +
            "if it keeps happening, use a shorter study or raise \"Context\" in Settings → AI.");
    }

    /// <summary>
    /// Repairs JSON that stopped part-way: cuts back to the last complete array element (or closed object)
    /// and closes whatever is still open. Incomplete items are dropped rather than half-kept.
    /// </summary>
    public static string? Salvage(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;
        var stack = new Stack<char>();
        bool inString = false, escaped = false;
        var lastSafe = -1;
        char[]? safeStack = null;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            switch (c)
            {
                case '"': inString = true; break;
                case '{': case '[': stack.Push(c); break;
                case '}': case ']':
                    if (stack.Count == 0) return null;
                    stack.Pop();
                    if (stack.Count == 0) return text[start..(i + 1)];
                    lastSafe = i; safeStack = stack.ToArray();
                    break;
                case ',':
                    // Between two array elements the previous one is complete.
                    if (stack.Count > 0 && stack.Peek() == '[') { lastSafe = i - 1; safeStack = stack.ToArray(); }
                    break;
            }
        }
        if (lastSafe < 0 || safeStack is null) return null;
        var sb = new System.Text.StringBuilder(text[start..(lastSafe + 1)].TrimEnd().TrimEnd(','));
        foreach (var open in safeStack) sb.Append(open == '{' ? '}' : ']');   // safeStack is top-first
        return sb.ToString();
    }

    /// <summary>Rough token estimate (≈3.5 characters per token for English).</summary>
    public static int EstimateTokens(string text) => (int)Math.Ceiling(text.Length / 3.5);

    /// <summary>How many characters of input fit in the context window, leaving room for the answer.</summary>
    public int InputCharBudget()
    {
        var o = _settings.Current.Ollama;
        var tokens = Math.Max(1500, Math.Clamp(o.ContextTokens, 2048, 131072) - Math.Clamp(o.MaxAnswerTokens, 256, 16384) - 400);
        return (int)(tokens * 3.2);
    }
}

public sealed class AiFormatException(string message) : InvalidOperationException(message);
