using LifeHub.Integrations.Email;
using LifeHub.Integrations.HomeAssistant;
using Microsoft.Extensions.Caching.Memory;

namespace LifeHub.Services;

/// <summary>Merges events from HA calendar entities and Microsoft 365 accounts, and creates events in the configured target.</summary>
public sealed class CalendarService
{
    private readonly HomeAssistantClient _ha;
    private readonly HomeAssistantCatalog _catalog;
    private readonly MicrosoftGraphClient _graph;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CalendarService> _log;

    public CalendarService(HomeAssistantClient ha, HomeAssistantCatalog catalog, MicrosoftGraphClient graph, SettingsService settings, Clock clock, IMemoryCache cache, ILogger<CalendarService> log)
    {
        _ha = ha; _catalog = catalog; _graph = graph;
        // Calendars chosen/connected in Settings should show up straight away, not after the cache expires.
        settings.Changed += InvalidateCache; _settings = settings; _clock = clock; _cache = cache; _log = log;
    }

    /// <summary>Errors from the last fetch, so the UI can say why a calendar is missing.</summary>
    public List<string> LastErrors { get; } = new();

    public async Task<List<CalendarEvent>> GetEventsAsync(DateOnly from, DateOnly toInclusive, bool bypassCache = false)
    {
        var key = $"cal:{from}:{toInclusive}";
        if (!bypassCache && _cache.TryGetValue(key, out List<CalendarEvent>? cached) && cached is not null) return cached;

        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var s = _settings.Current;
        var tasks = new List<Task<List<CalendarEvent>>>();
        var errors = new List<string>();

        if (_ha.IsConfigured)
        {
            try { await _catalog.GetAsync(); } catch { /* names fall back to tidied ids */ }
            foreach (var cal in s.HomeAssistant.CalendarEntities.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                var name = _catalog.NameOf(cal);
                tasks.Add(Safe(name, async () =>
                {
                    var evs = await _ha.GetCalendarEventsAsync(cal, start, end, _clock);
                    foreach (var e in evs) e.Source = name;
                    return evs;
                }, errors));
            }
        }

        foreach (var acc in s.Email.Accounts.Where(a => a.Enabled && a.UseForCalendar && a.Kind == EmailAccountKind.MicrosoftGraph && !string.IsNullOrEmpty(a.GraphRefreshToken)))
            tasks.Add(Safe(acc.Name, () => _graph.GetEventsAsync(acc, start, end, _clock), errors));

        var all = (await Task.WhenAll(tasks)).SelectMany(x => x)
            .Where(e => e.End > start && e.Start < end)
            .OrderBy(e => e.Start).ToList();

        lock (LastErrors) { LastErrors.Clear(); LastErrors.AddRange(errors); }
        _cache.Set(key, all, TimeSpan.FromMinutes(5));
        return all;
    }

    private async Task<List<CalendarEvent>> Safe(string name, Func<Task<List<CalendarEvent>>> f, List<string> errors)
    {
        try { return await f(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Calendar {Name} failed", name);
            lock (errors) errors.Add($"{name}: {ex.Message}");
            return new();
        }
    }

    public bool CanCreateEvents
    {
        get
        {
            var s = _settings.Current;
            return (_ha.IsConfigured && !string.IsNullOrWhiteSpace(s.HomeAssistant.DefaultCalendarEntity)) ||
                   s.Email.Accounts.Any(a => a.Enabled && a.UseForCalendar && a.Kind == EmailAccountKind.MicrosoftGraph && !string.IsNullOrEmpty(a.GraphRefreshToken));
        }
    }

    /// <summary>Creates an event in the HA default calendar, or else the first Microsoft 365 account marked for calendar use. Returns where it went.</summary>
    public async Task<string> CreateEventAsync(string title, DateTime startLocal, DateTime endLocal, string description = "", bool allDay = false)
    {
        var s = _settings.Current;
        string where;
        if (_ha.IsConfigured && !string.IsNullOrWhiteSpace(s.HomeAssistant.DefaultCalendarEntity))
        {
            await _ha.CreateCalendarEventAsync(s.HomeAssistant.DefaultCalendarEntity, title, startLocal, endLocal, description, allDay);
            where = s.HomeAssistant.DefaultCalendarEntity;
        }
        else
        {
            var acc = s.Email.Accounts.FirstOrDefault(a => a.Enabled && a.UseForCalendar && a.Kind == EmailAccountKind.MicrosoftGraph && !string.IsNullOrEmpty(a.GraphRefreshToken))
                      ?? throw new InvalidOperationException("No calendar to write to. Set a default HA calendar or enable 'Use for calendar' on a Microsoft 365 account.");
            await _graph.CreateEventAsync(acc, title, startLocal, endLocal, description, allDay, _clock);
            where = acc.Name;
        }
        InvalidateCache();
        return where;
    }

    public void InvalidateCache()
    {
        if (_cache is MemoryCache mc) mc.Compact(1.0);
    }
}
