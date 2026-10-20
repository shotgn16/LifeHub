using System.Text.Json;

namespace LifeHub.Integrations.HomeAssistant;

/// <summary>An entity as you'd recognise it: its HA name, room and device — not its ID.</summary>
public sealed class HaEntity
{
    public required HaState State { get; init; }
    public string Id => State.EntityId;
    public string Domain => State.Domain;
    public string Name { get; init; } = "";
    public string Area { get; init; } = "";
    public string Device { get; init; } = "";
    public string? DeviceClass => State.Attr("device_class");
    public string? Unit => State.Unit;
    public bool IsOn => State.State is "on" or "open" or "playing" or "home" or "unlocked" or "heat" or "cool" or "auto";
    public bool Unavailable => State.State is "unavailable" or "unknown";

    /// <summary>Friendly state text, e.g. "On · 78%", "21.5 °C", "Home".</summary>
    public string StateText
    {
        get
        {
            var st = State.State;
            if (Unavailable) return "Unavailable";
            switch (Domain)
            {
                case "light" when st == "on" && State.Attr("brightness") is { } b && double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bv):
                    return $"On · {Math.Round(bv / 2.55)}%";
                case "person" or "device_tracker":
                    return st switch { "home" => "Home", "not_home" => "Away", _ => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(st.Replace('_', ' ')) };
                case "climate":
                    var cur = State.Attr("current_temperature");
                    var target = State.Attr("temperature");
                    return $"{Title(st)}{(cur is null ? "" : $" · {cur}°")}{(target is null ? "" : $" → {target}°")}";
                case "sensor":
                    return $"{st}{(string.IsNullOrEmpty(Unit) ? "" : " " + Unit)}";
                case "binary_sensor":
                    return DeviceClass switch
                    {
                        "door" or "window" or "garage_door" or "opening" => st == "on" ? "Open" : "Closed",
                        "motion" or "occupancy" or "presence" => st == "on" ? "Detected" : "Clear",
                        "moisture" => st == "on" ? "Wet" : "Dry",
                        _ => st == "on" ? "On" : "Off"
                    };
                default:
                    return Title(st);
            }
        }
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');
}

/// <summary>
/// Reads HA once and returns entities with friendly names, rooms (areas) and device names, cached briefly.
/// Areas/devices come from a single HA template call; if that's not allowed, names still work.
/// </summary>
public sealed class HomeAssistantCatalog
{
    private const string AreaTemplate = """
        {%- for s in states -%}
        {%- if s.domain in ['light','switch','fan','cover','climate','lock','media_player','sensor','binary_sensor','person','scene','script','calendar','todo','vacuum','input_boolean','device_tracker'] and not is_hidden_entity(s.entity_id) -%}
        {%- set d = device_id(s.entity_id) -%}
        {{ s.entity_id }}|{{ area_name(s.entity_id) or '' }}|{{ ((device_attr(d, 'name_by_user') or device_attr(d, 'name') or '') if d else '') | replace('|', '/') }}
        {% endif -%}
        {%- endfor -%}
        """;

    private readonly HomeAssistantClient _ha;
    private readonly ILogger<HomeAssistantCatalog> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<HaEntity> _cache = new();
    private Dictionary<string, (string Area, string Device)> _areas = new();
    private DateTime _statesAt = DateTime.MinValue, _areasAt = DateTime.MinValue;

    public HomeAssistantCatalog(HomeAssistantClient ha, ILogger<HomeAssistantCatalog> log)
    {
        _ha = ha; _log = log;
    }

    /// <summary>True if area/device names could be read (needs HA's template API).</summary>
    public bool HasAreas => _areas.Count > 0;

    public async Task<List<HaEntity>> GetAsync(bool refresh = false, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!refresh && DateTime.UtcNow - _statesAt < TimeSpan.FromSeconds(15)) return _cache;
            var states = await _ha.GetStatesAsync();
            if (refresh || DateTime.UtcNow - _areasAt > TimeSpan.FromMinutes(10))
            {
                try
                {
                    var text = await _ha.RenderTemplateAsync(AreaTemplate, ct);
                    _areas = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(l => l.Split('|', 3))
                        .Where(p => p.Length == 3 && p[0].Contains('.'))
                        .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => (g.First()[1], g.First()[2]));
                }
                catch (Exception ex) { _log.LogInformation("Area lookup unavailable: {Message}", ex.Message); }
                _areasAt = DateTime.UtcNow;
            }
            _cache = states.Select(s =>
            {
                var (area, device) = _areas.TryGetValue(s.EntityId, out var a) ? a : ("", "");
                return new HaEntity { State = s, Name = CleanName(s, device), Area = area, Device = device };
            }).ToList();
            _statesAt = DateTime.UtcNow;
            return _cache;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Friendly name; falls back to a tidied entity id, never shows "light.xyz".</summary>
    private static string CleanName(HaState s, string device)
    {
        var name = s.Attr("friendly_name");
        if (string.IsNullOrWhiteSpace(name))
        {
            var id = s.EntityId[(s.EntityId.IndexOf('.') + 1)..].Replace('_', ' ');
            name = id.Length > 0 ? char.ToUpperInvariant(id[0]) + id[1..] : s.EntityId;
        }
        return name.Trim();
    }

    /// <summary>Name for any entity id, using the cache (falls back to a tidied id).</summary>
    public string NameOf(string entityId)
    {
        var e = _cache.FirstOrDefault(x => x.Id == entityId);
        if (e is not null) return e.Name;
        var id = entityId.Contains('.') ? entityId[(entityId.IndexOf('.') + 1)..] : entityId;
        id = id.Replace('_', ' ');
        return id.Length > 0 ? char.ToUpperInvariant(id[0]) + id[1..] : entityId;
    }

    /// <summary>Friendly label for a notify service, e.g. notify.mobile_app_pixel_8 → "Pixel 8" (from the phone's device tracker).</summary>
    public string NotifyLabel(string service)
    {
        const string prefix = "notify.mobile_app_";
        if (service.StartsWith(prefix))
        {
            var slug = service[prefix.Length..];
            var tracker = _cache.FirstOrDefault(e => e.Id == "device_tracker." + slug);
            if (tracker is not null) return string.IsNullOrEmpty(tracker.Device) ? tracker.Name : tracker.Device;
            return NameOf("x." + slug);
        }
        return NameOf(service);
    }

    /// <summary>HA calendar supports creating events (CalendarEntityFeature.CREATE_EVENT = 1).</summary>
    public static bool CanCreateEvents(HaEntity cal) => (Features(cal) & 1) != 0;

    public static int Features(HaEntity e) =>
        int.TryParse(e.State.Attr("supported_features"), out var f) ? f : 0;
}
