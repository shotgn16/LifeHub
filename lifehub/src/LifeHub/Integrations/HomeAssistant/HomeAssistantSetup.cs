namespace LifeHub.Integrations.HomeAssistant;

/// <summary>
/// Picks sensible defaults from your Home Assistant so there's (almost) nothing to configure:
/// everyone in People, all calendars (and one you can add events to), every phone for notifications,
/// your main power/energy sensors, and HA's shopping list / a task list for two-way sync.
/// </summary>
public sealed class HomeAssistantSetup
{
    private readonly HomeAssistantCatalog _catalog;
    private readonly HomeAssistantClient _client;
    private readonly SettingsService _settings;

    public HomeAssistantSetup(HomeAssistantCatalog catalog, HomeAssistantClient client, SettingsService settings)
    {
        _catalog = catalog; _client = client; _settings = settings;
    }

    /// <summary>Fills in anything not yet chosen (or everything when <paramref name="redo"/>). Returns a summary line.</summary>
    public async Task<string> AutoConfigureAsync(bool redo, CancellationToken ct = default)
    {
        var all = await _catalog.GetAsync(refresh: true, ct);
        var live = all.Where(e => !e.Unavailable).ToList();
        var current = _settings.Current.HomeAssistant;

        var people = all.Where(e => e.Domain == "person").Select(e => e.Id).ToList();
        var calendars = all.Where(e => e.Domain == "calendar").ToList();
        var writable = calendars.Where(HomeAssistantCatalog.CanCreateEvents)
            .OrderByDescending(c => Score(c.Name, "lifehub", "personal", "family", "home", "my"))
            .ThenBy(c => c.Name).FirstOrDefault();
        var phones = all.Where(e => e.Domain == "device_tracker" && (e.State.Attr("source_type") == "gps" || e.Device.Length > 0))
            .Select(e => "notify.mobile_app_" + e.Id["device_tracker.".Length..]).Distinct().ToList();
        try
        {
            // Use HA's real list of notify services where possible (phones only).
            var services = await _client.GetServicesAsync("notify");
            if (services.Count > 0) phones = services.Where(s => s.StartsWith("notify.mobile_app_")).ToList();
        }
        catch { /* fall back to device trackers */ }

        var power = live.Where(e => e.Domain == "sensor" && (e.DeviceClass == "power" || e.Unit is "W" or "kW"))
            .OrderByDescending(e => Score(e.Name, "house", "home", "grid", "total", "consumption", "whole", "import") - Score(e.Name, "solar", "battery", "pv", "export") * 2)
            .FirstOrDefault();
        var energyToday = live.Where(e => e.Domain == "sensor" && (e.DeviceClass == "energy" || e.Unit is "kWh" or "Wh"))
            .Where(e => Score(e.Name, "today", "daily") > 0)
            .OrderByDescending(e => Score(e.Name, "house", "home", "grid", "total", "consumption", "import") - Score(e.Name, "solar", "export"))
            .FirstOrDefault();
        var solar = live.FirstOrDefault(e => e.Domain == "sensor" && (e.DeviceClass == "power" || e.Unit is "W" or "kW") &&
                                             Score(e.Name, "solar", "pv") > 0 && e.Id != power?.Id);

        var todos = all.Where(e => e.Domain == "todo").ToList();
        var shopping = todos.FirstOrDefault(t => t.Id == "todo.shopping_list") ?? todos.FirstOrDefault(t => Score(t.Name, "shopping", "groceries") > 0);
        var tasks = todos.Where(t => t.Id != shopping?.Id).FirstOrDefault(t => Score(t.Name, "lifehub", "task", "to do", "todo", "jobs") > 0);

        var notes = new List<string>();
        await _settings.UpdateAsync(s =>
        {
            var h = s.HomeAssistant;
            if (redo || h.PresenceEntities.Count == 0) { h.PresenceEntities = people; notes.Add($"{people.Count} people"); }
            if (redo || h.CalendarEntities.Count == 0) { h.CalendarEntities = calendars.Select(c => c.Id).ToList(); notes.Add($"{calendars.Count} calendars"); }
            if ((redo || string.IsNullOrEmpty(h.DefaultCalendarEntity)) && writable is not null) { h.DefaultCalendarEntity = writable.Id; notes.Add($"new events → {writable.Name}"); }
            if (redo || (h.NotifyServices.Count == 0 && string.IsNullOrEmpty(h.NotifyService))) { h.NotifyServices = phones; notes.Add($"{phones.Count} phone(s)"); }
            if (redo || h.EnergySensors.Count == 0)
            {
                h.EnergySensors = new[] { power, energyToday, solar }.Where(e => e is not null)
                    .Select(e => new EnergySensor { EntityId = e!.Id, Label = e.Name }).ToList();
                if (h.EnergySensors.Count > 0) notes.Add($"{h.EnergySensors.Count} energy sensor(s)");
            }
            if ((redo || string.IsNullOrEmpty(h.ShoppingTodoEntity)) && shopping is not null) { h.ShoppingTodoEntity = shopping.Id; notes.Add($"shopping list ↔ {shopping.Name}"); }
            if ((redo || string.IsNullOrEmpty(h.TasksTodoEntity)) && tasks is not null) { h.TasksTodoEntity = tasks.Id; notes.Add($"tasks ↔ {tasks.Name}"); }
            h.AutoSetupDone = true;
        });
        return notes.Count == 0 ? "Everything was already set up." : "Set up: " + string.Join(", ", notes) + ".";
    }

    private static int Score(string name, params string[] words)
    {
        var n = name.ToLowerInvariant();
        return words.Count(w => n.Contains(w));
    }
}
