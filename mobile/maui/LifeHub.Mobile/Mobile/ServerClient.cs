using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeHub.Mobile.Mobile;

/// <summary>
/// Connected to a LifeHub server: signs in with your API key, keeps an offline copy (tasks, shopping, notes, meals)
/// and the changes you make offline (encrypted on the phone), sends them when the server is back, and shows the
/// server's notifications on the phone.
/// </summary>
public sealed class ServerClient
{
    private readonly MobileState _state;
    private readonly DataCipher _cipher;
    private readonly DataPaths _paths;
    private readonly IDeviceNotifier _notifier;
    private readonly IHttpClientFactory _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ServerClient(MobileState state, DataCipher cipher, DataPaths paths, IDeviceNotifier notifier, IHttpClientFactory http)
    {
        _state = state; _cipher = cipher; _paths = paths; _notifier = notifier; _http = http;
    }

    private string SnapshotFile => Path.Combine(_paths.Root, "server-snapshot.json");
    private string OutboxFile => Path.Combine(_paths.Root, "server-outbox.json");

    public string LastError { get; private set; } = "";
    public string LastSyncUtc { get => Preferences.Default.Get("serverSync", ""); private set => Preferences.Default.Set("serverSync", value); }
    public bool Online { get; private set; }

    private HttpClient Client(int seconds = 20)
    {
        var c = _http.CreateClient("server");
        c.BaseAddress = new Uri(_state.ServerUrl.TrimEnd('/') + "/");
        c.Timeout = TimeSpan.FromSeconds(seconds);
        c.DefaultRequestHeaders.Add("X-Api-Key", _state.ApiKey);
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LifeHub-App", AppInfo.VersionString));
        return c;
    }

    /// <summary>Checks the address and key; returns who you're signed in as.</summary>
    public async Task<string> TestAsync(string url, string key)
    {
        using var c = new HttpClient { BaseAddress = new Uri(url.Trim().TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("X-Api-Key", key.Trim());
        using var r = await c.GetAsync("api/v1/me");
        if (r.StatusCode == System.Net.HttpStatusCode.Unauthorized) throw new InvalidOperationException("The API key wasn't accepted.");
        r.EnsureSuccessStatusCode();
        var me = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        return me.TryGetProperty("name", out var n) ? n.GetString() ?? "you" : "you";
    }

    public async Task<bool> ReachableAsync()
    {
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var r = await c.GetAsync(_state.ServerUrl.TrimEnd('/') + "/health");
            return Online = r.IsSuccessStatusCode;
        }
        catch { return Online = false; }
    }

    // ---------------------------------------------------------------- offline copy and outbox
    public JsonNode? Snapshot() => File.Exists(SnapshotFile) ? JsonNode.Parse(_cipher.ReadText(SnapshotFile)) : null;
    private JsonArray Outbox() => File.Exists(OutboxFile) ? (JsonNode.Parse(_cipher.ReadText(OutboxFile)) as JsonArray ?? new()) : new();

    /// <summary>A change made on the offline screen (task.add, task.complete, shopping.add, shopping.check, note.add).</summary>
    public void Queue(string changeJson)
    {
        var change = JsonNode.Parse(changeJson)!.AsObject();
        if (change["id"] is null) change["id"] = Guid.NewGuid().ToString();
        change["at"] ??= DateTime.UtcNow.ToString("o");
        var box = Outbox();
        box.Add(change);
        _cipher.WriteText(OutboxFile, box.ToJsonString());
    }

    /// <summary>What the offline screen shows.</summary>
    public string StateJson() => new JsonObject
    {
        ["snapshot"] = Snapshot()?.DeepClone(),
        ["pending"] = Outbox().DeepClone(),
        ["lastSyncUtc"] = LastSyncUtc,
        ["online"] = Online,
        ["lastError"] = LastError
    }.ToJsonString();

    /// <summary>Sends waiting changes (each has its own id, so nothing is applied twice) and refreshes the offline copy.</summary>
    public async Task<string> SyncAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_state.ServerUrl)) return "Not connected";
        await _gate.WaitAsync(ct);
        try
        {
            var box = Outbox();
            JsonNode? snapshot;
            if (box.Count > 0)
            {
                using var r = await Client().PostAsync("api/v1/sync", JsonHttp.Body(new JsonObject { ["changes"] = box.DeepClone() }), ct);
                r.EnsureSuccessStatusCode();
                var j = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
                snapshot = j?["snapshot"];
                // Keep only changes the server didn't confirm.
                var done = (j?["results"] as JsonArray ?? new()).Select(x => x?["id"]?.ToString()).ToHashSet();
                var left = new JsonArray(box.Where(c => !done.Contains(c?["id"]?.ToString())).Select(c => c!.DeepClone()).ToArray());
                _cipher.WriteText(OutboxFile, left.ToJsonString());
            }
            else
            {
                using var r = await Client().GetAsync("api/v1/offline", ct);
                r.EnsureSuccessStatusCode();
                snapshot = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct));
            }
            if (snapshot is not null) _cipher.WriteText(SnapshotFile, snapshot.ToJsonString());
            LastSyncUtc = DateTime.UtcNow.ToString("o");
            LastError = "";
            Online = true;
            return box.Count > 0 ? $"sent {box.Count} change{(box.Count == 1 ? "" : "s")}" : "up to date";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LastError = ex.Message;
            Online = false;
            return "offline: " + ex.Message;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// The regular check: send changes, refresh the offline copy, ask the server to check email (when the app opens)
    /// and show any new notifications meant for the phone.
    /// </summary>
    public async Task<string> CheckAsync(bool openedApp, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_state.ServerUrl) || string.IsNullOrEmpty(_state.ApiKey)) return "Not connected";
        var sync = await SyncAsync(ct);
        if (!Online) return sync;
        try
        {
            if (openedApp) { using var _ = await Client().PostAsync("api/v1/email/check", null, ct); }
            var since = _state.NoticesSinceUtc;
            using var r = await Client().GetAsync($"api/v1/notices?since={Uri.EscapeDataString(since.ToString("o"))}", ct);
            r.EnsureSuccessStatusCode();
            var list = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement;
            var shown = 0;
            foreach (var n in list.EnumerateArray())
            {
                var created = n.GetProperty("createdUtc").GetDateTime();
                if (created > since) since = created;
                var phone = !n.TryGetProperty("phone", out var p) || p.ValueKind != JsonValueKind.False;   // servers before 3.1 have no flag
                if (!phone || (n.TryGetProperty("read", out var read) && read.GetBoolean())) continue;
                await ((DeviceNotifier)_notifier).Show(n.GetProperty("title").GetString() ?? "", n.GetProperty("message").GetString() ?? "",
                    n.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "", n.GetProperty("link").GetString() ?? "", n.GetProperty("id").GetString());
                shown++;
            }
            _state.NoticesSinceUtc = since;
            return $"{sync}; {shown} notification{(shown == 1 ? "" : "s")}";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { return $"{sync}; notifications failed: {ex.Message}"; }
    }
}
