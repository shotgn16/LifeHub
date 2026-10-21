using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeHub.Integrations.HomeAssistant;

public sealed class HaState
{
    [JsonPropertyName("entity_id")] public string EntityId { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";
    [JsonPropertyName("attributes")] public JsonElement Attributes { get; set; }
    [JsonPropertyName("last_changed")] public DateTime LastChanged { get; set; }

    public string Domain => EntityId.Split('.')[0];
    public string Name => Attr("friendly_name") ?? EntityId;
    public string? Unit => Attr("unit_of_measurement");

    public string? Attr(string key) =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty(key, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()
            : null;

    public double? Number => double.TryParse(State, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
}

public sealed class HaCalendar
{
    [JsonPropertyName("entity_id")] public string EntityId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public sealed class HaException(string message) : Exception(message);

/// <summary>
/// Thin wrapper over the Home Assistant REST API. When running as an add-on it uses the
/// Supervisor proxy (no token needed); otherwise the URL + long-lived access token from Settings.
/// </summary>
public sealed class HomeAssistantClient
{
    private readonly IHttpClientFactory _http;
    private readonly SettingsService _settings;
    private readonly DataPaths _paths;
    private readonly ILogger<HomeAssistantClient> _log;

    public HomeAssistantClient(IHttpClientFactory http, SettingsService settings, DataPaths paths, ILogger<HomeAssistantClient> log)
    {
        _http = http; _settings = settings; _paths = paths; _log = log;
    }

    private string? SupervisorToken => Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN");

    public bool IsConfigured
    {
        get
        {
            var s = _settings.Current.HomeAssistant;
            return s.Enabled && (_paths.IsHomeAssistantAddon || (!string.IsNullOrWhiteSpace(s.Url) && !string.IsNullOrWhiteSpace(s.Token)));
        }
    }

    /// <summary>"Supervisor" when using the add-on proxy, else the configured URL.</summary>
    public string ConnectionDescription => _paths.IsHomeAssistantAddon && string.IsNullOrWhiteSpace(_settings.Current.HomeAssistant.Token)
        ? "Home Assistant Supervisor (add-on)" : _settings.Current.HomeAssistant.Url;

    private HttpClient Client()
    {
        var s = _settings.Current.HomeAssistant;
        var c = _http.CreateClient("ha");
        c.Timeout = TimeSpan.FromSeconds(20);
        // A token typed into Settings wins, so the add-on can also talk to a different HA instance.
        if (_paths.IsHomeAssistantAddon && string.IsNullOrWhiteSpace(s.Token))
        {
            c.BaseAddress = new Uri("http://supervisor/core/api/");
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SupervisorToken);
        }
        else
        {
            c.BaseAddress = new Uri(s.Url.TrimEnd('/') + "/api/");
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", s.Token);
        }
        return c;
    }

    private static async Task<T> Read<T>(HttpResponseMessage r)
    {
        if (!r.IsSuccessStatusCode)
        {
            var body = await r.Content.ReadAsStringAsync();
            throw new HaException($"Home Assistant returned {(int)r.StatusCode}: {body[..Math.Min(body.Length, 300)]}");
        }
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    public async Task<string> PingAsync()
    {
        using var r = await Client().GetAsync("");
        var doc = await Read<JsonElement>(r);
        return doc.TryGetProperty("message", out var m) ? m.GetString() ?? "OK" : "OK";
    }

    public async Task<List<HaState>> GetStatesAsync()
    {
        using var r = await Client().GetAsync("states");
        return await Read<List<HaState>>(r);
    }

    public async Task<HaState?> GetStateAsync(string entityId)
    {
        using var r = await Client().GetAsync("states/" + entityId);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await Read<HaState>(r);
    }

    /// <summary>Creates/updates an entity state (used to publish LifeHub sensors into HA).</summary>
    public async Task SetStateAsync(string entityId, string state, Dictionary<string, object?> attributes)
    {
        using var r = await Client().PostAsync("states/" + entityId, JsonHttp.Body(new { state, attributes }));
        await Read<JsonElement>(r);
    }

    public async Task CallServiceAsync(string domain, string service, object data)
    {
        using var r = await Client().PostAsync($"services/{domain}/{service}", JsonHttp.Body(data));
        await Read<JsonElement>(r);
    }

    /// <summary>Lists services of one domain (used to suggest notify.* targets).</summary>
    public async Task<List<string>> GetServicesAsync(string domain)
    {
        using var r = await Client().GetAsync("services");
        var all = await Read<List<JsonElement>>(r);
        return all.Where(d => d.GetProperty("domain").GetString() == domain)
            .SelectMany(d => d.GetProperty("services").EnumerateObject().Select(s => $"{domain}.{s.Name}"))
            .OrderBy(x => x).ToList();
    }

    /// <summary>Renders a Jinja template in HA (used to read areas and device names in one call).</summary>
    public async Task<string> RenderTemplateAsync(string template, CancellationToken ct = default)
    {
        using var r = await Client().PostAsync("template", JsonHttp.Body(new { template }), ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new HaException($"Template failed ({(int)r.StatusCode}): {body[..Math.Min(200, body.Length)]}");
        return body;
    }

    /// <summary>Calls a service that returns data (e.g. todo.get_items) and returns its response object.</summary>
    public async Task<JsonElement> CallServiceWithResponseAsync(string domain, string service, object data, CancellationToken ct = default)
    {
        using var r = await Client().PostAsync($"services/{domain}/{service}?return_response", JsonHttp.Body(data), ct);
        var j = await Read<JsonElement>(r);
        return j.ValueKind == JsonValueKind.Object && j.TryGetProperty("service_response", out var sr) ? sr : j;
    }

    public Task ToggleAsync(string entityId) =>
        CallServiceAsync(entityId.Split('.')[0], "toggle", new Dictionary<string, object> { ["entity_id"] = entityId });

    public async Task<List<HaCalendar>> GetCalendarsAsync()
    {
        using var r = await Client().GetAsync("calendars");
        return await Read<List<HaCalendar>>(r);
    }

    public async Task<List<CalendarEvent>> GetCalendarEventsAsync(string entityId, DateTime startLocal, DateTime endLocal, Clock clock)
    {
        var url = $"calendars/{entityId}?start={Uri.EscapeDataString(clock.Iso(startLocal))}&end={Uri.EscapeDataString(clock.Iso(endLocal))}";
        using var r = await Client().GetAsync(url);
        var items = await Read<List<JsonElement>>(r);
        var list = new List<CalendarEvent>();
        foreach (var e in items)
        {
            var (start, allDay) = ParseHaTime(e.GetProperty("start"), clock);
            var (end, _) = ParseHaTime(e.GetProperty("end"), clock);
            list.Add(new CalendarEvent
            {
                Title = e.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                Description = e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : "",
                Location = e.TryGetProperty("location", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "",
                Start = start, End = end, AllDay = allDay, Source = entityId
            });
        }
        return list;
    }

    private static (DateTime, bool) ParseHaTime(JsonElement el, Clock clock)
    {
        if (el.TryGetProperty("dateTime", out var dt))
        {
            var dto = DateTimeOffset.Parse(dt.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            return (clock.ToLocal(dto.UtcDateTime), false);
        }
        return (DateTime.Parse(el.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture), true);
    }

    /// <summary>Creates an event via calendar.create_event (Local Calendar, Google Calendar, CalDAV…).</summary>
    public Task CreateCalendarEventAsync(string calendarEntity, string summary, DateTime startLocal, DateTime endLocal, string description, bool allDay)
    {
        var data = new Dictionary<string, object> { ["entity_id"] = calendarEntity, ["summary"] = summary };
        if (!string.IsNullOrWhiteSpace(description)) data["description"] = description;
        if (allDay)
        {
            data["start_date"] = startLocal.ToString("yyyy-MM-dd");
            data["end_date"] = (endLocal.Date <= startLocal.Date ? startLocal.Date.AddDays(1) : endLocal.Date).ToString("yyyy-MM-dd");
        }
        else
        {
            data["start_date_time"] = startLocal.ToString("yyyy-MM-dd HH:mm:ss");
            data["end_date_time"] = endLocal.ToString("yyyy-MM-dd HH:mm:ss");
        }
        return CallServiceAsync("calendar", "create_event", data);
    }

    /// <summary>
    /// Sends to the chosen phones (or just <paramref name="onlyTo"/> when given and configured).
    /// Uses Companion-app extras: tapping opens LifeHub, a tag replaces the previous notice of the same
    /// kind instead of piling up, and a group keeps LifeHub notices together.
    /// </summary>
    public async Task NotifyAsync(string title, string message, string? link = null, string? tag = null, IEnumerable<string>? onlyTo = null)
    {
        var s = _settings.Current.HomeAssistant;
        var targets = s.NotifyServices.Concat(string.IsNullOrWhiteSpace(s.NotifyService) ? Array.Empty<string>() : new[] { s.NotifyService })
            .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        if (onlyTo is not null)
        {
            var wanted = onlyTo.ToHashSet();
            var narrowed = targets.Where(wanted.Contains).ToList();
            if (narrowed.Count > 0) targets = narrowed;
        }
        if (!IsConfigured || targets.Count == 0) return;

        var data = new Dictionary<string, object> { ["group"] = "lifehub", ["notification_icon"] = "mdi:view-dashboard-variant-outline" };
        if (!string.IsNullOrEmpty(tag)) data["tag"] = "lifehub-" + tag;
        var url = ClickUrl(link);
        if (url is not null) { data["clickAction"] = url; data["url"] = url; }
        var payload = new Dictionary<string, object> { ["title"] = title, ["message"] = message, ["data"] = data };
        foreach (var svc in targets)
        {
            var parts = svc.Split('.', 2);
            var (domain, service) = parts.Length == 2 ? (parts[0], parts[1]) : ("notify", parts[0]);
            try { await CallServiceAsync(domain, service, payload); }
            catch (Exception ex) { _log.LogWarning(ex, "HA notify via {Service} failed", svc); }
        }
    }

    /// <summary>Your LifeHub address + page, or the add-on's panel inside the HA app.</summary>
    private string? ClickUrl(string? link)
    {
        var baseUrl = _settings.Current.General.PublicUrl?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(baseUrl)) return string.IsNullOrEmpty(link) ? baseUrl : $"{baseUrl}/{link.TrimStart('/')}";
        var slug = Environment.GetEnvironmentVariable("HOSTNAME") is { Length: > 0 } h ? h.Replace('-', '_') : "local_lifehub";
        return _paths.IsHomeAssistantAddon ? $"/hassio/ingress/{slug}" : null;
    }

    /// <summary>Notify services for the phones that belong to a person (from the person's device trackers).</summary>
    public async Task<List<string>> PhonesOfAsync(string personEntity)
    {
        var person = await GetStateAsync(personEntity);
        if (person is null || person.Attributes.ValueKind != JsonValueKind.Object ||
            !person.Attributes.TryGetProperty("device_trackers", out var trackers) || trackers.ValueKind != JsonValueKind.Array) return new();
        return trackers.EnumerateArray().Select(t => t.GetString() ?? "")
            .Where(t => t.StartsWith("device_tracker.")).Select(t => "notify.mobile_app_" + t["device_tracker.".Length..]).ToList();
    }

    /// <summary>Raw state changes (UTC time, state) since a time — e.g. which app was in use.</summary>
    public async Task<List<(DateTime Utc, string State)>> GetStateHistoryAsync(string entityId, DateTime sinceUtc)
    {
        var url = $"history/period/{sinceUtc:yyyy-MM-ddTHH:mm:ss}Z?filter_entity_id={entityId}&minimal_response&no_attributes";
        using var r = await Client().GetAsync(url);
        var data = await Read<List<List<JsonElement>>>(r);
        var list = new List<(DateTime, string)>();
        foreach (var e in data.FirstOrDefault() ?? new())
        {
            if (!e.TryGetProperty("state", out var st)) continue;
            var ts = e.TryGetProperty("last_changed", out var lc) ? lc.GetDateTime().ToUniversalTime() : DateTime.UtcNow;
            list.Add((ts, st.GetString() ?? ""));
        }
        return list;
    }

    /// <summary>State history for charts (minimal response, no attributes).</summary>
    public async Task<List<(DateTime Time, double Value)>> GetNumericHistoryAsync(string entityId, DateTime sinceUtc, Clock clock)
    {
        var url = $"history/period/{sinceUtc:yyyy-MM-ddTHH:mm:ss}Z?filter_entity_id={entityId}&minimal_response&no_attributes";
        using var r = await Client().GetAsync(url);
        var data = await Read<List<List<JsonElement>>>(r);
        var points = new List<(DateTime, double)>();
        foreach (var e in data.FirstOrDefault() ?? new())
        {
            if (!e.TryGetProperty("state", out var st)) continue;
            if (!double.TryParse(st.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) continue;
            var ts = e.TryGetProperty("last_changed", out var lc) ? lc.GetDateTime() : DateTime.UtcNow;
            points.Add((clock.ToLocal(ts.ToUniversalTime()), v));
        }
        return points;
    }
}
