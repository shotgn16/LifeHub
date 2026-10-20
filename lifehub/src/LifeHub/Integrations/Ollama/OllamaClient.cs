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
    /// Returns the parsed JSON object.
    /// </summary>
    public async Task<JsonElement> ChatJsonAsync(string system, string user, JsonObject schema, CancellationToken ct = default)
    {
        var o = _settings.Current.Ollama;
        var request = new JsonObject
        {
            ["model"] = o.Model,
            ["stream"] = false,
            ["format"] = schema,
            ["options"] = new JsonObject { ["temperature"] = o.Temperature },
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }
            }
        };
        using var r = await Client().PostAsync("api/chat", JsonHttp.Body(request), ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama returned {(int)r.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        var content = JsonDocument.Parse(body).RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
        return ParseLenient(content);
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
