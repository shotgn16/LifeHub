namespace LifeHub.Services;

public sealed class DaySegment
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public PeriodKind? Kind { get; set; }
    public string Label { get; set; } = "";
    public CalendarEvent? Event { get; set; }
    public TaskItem? Task { get; set; }
    public bool IsEvent => Event is not null;
}

public sealed record CurrentPeriod(string Label, PeriodKind? Kind, CalendarEvent? Event, DateTime? Until, string NextLabel, DateTime? NextStart);

/// <summary>Works out what kind of time it is (work / free / busy) from the weekly template plus calendar events.</summary>
public sealed class ScheduleService
{
    private readonly SettingsService _settings;
    private readonly CalendarService _calendar;
    private readonly Clock _clock;

    public ScheduleService(SettingsService settings, CalendarService calendar, Clock clock)
    {
        _settings = settings; _calendar = calendar; _clock = clock;
    }

    public bool IsDayOff(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        var words = _settings.Current.Schedule.DayOffKeywords;
        return events.Any(e => e.AllDay && DateOnly.FromDateTime(e.Start) <= date && DateOnly.FromDateTime(e.End) > date &&
                               words.Any(w => e.Title.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The template periods for a day, with Work turned into Free on days off.</summary>
    public List<DaySegment> Periods(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        var dayOff = IsDayOff(date, events);
        return _settings.Current.Schedule.Blocks
            .Where(b => b.Days.Contains(date.DayOfWeek))
            .Select(b =>
            {
                var start = date.ToDateTime(b.Start);
                var end = b.End <= b.Start ? date.AddDays(1).ToDateTime(b.End) : date.ToDateTime(b.End);
                var kind = dayOff && b.Kind == PeriodKind.Work ? PeriodKind.Free : b.Kind;
                var label = dayOff && b.Kind == PeriodKind.Work ? "Day off" : (string.IsNullOrWhiteSpace(b.Label) ? b.Kind.ToString() : b.Label);
                return new DaySegment { Start = start, End = end, Kind = kind, Label = label };
            })
            .OrderBy(s => s.Start).ToList();
    }

    public async Task<List<CalendarEvent>> EventsForAsync(DateOnly date) =>
        (await _calendar.GetEventsAsync(date, date)).ToList();

    /// <summary>Periods and timed events for a day, merged into one timeline (events listed alongside periods).</summary>
    public async Task<(List<DaySegment> Periods, List<CalendarEvent> Events)> DayAsync(DateOnly date)
    {
        var events = await EventsForAsync(date);
        return (Periods(date, events), events);
    }

    public async Task<CurrentPeriod> CurrentAsync()
    {
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now);
        var events = await _calendar.GetEventsAsync(today, today.AddDays(1));
        var current = events.FirstOrDefault(e => !e.AllDay && e.Start <= now && e.End > now);
        var periods = Periods(today, events).Concat(Periods(today.AddDays(1), events)).ToList();
        var period = periods.LastOrDefault(p => p.Start <= now && p.End > now);
        var next = periods.FirstOrDefault(p => p.Start > now);

        if (current is not null)
            return new CurrentPeriod("Busy: " + current.Title, period?.Kind, current, current.End, next?.Label ?? "", next?.Start);
        if (period is not null)
            return new CurrentPeriod(period.Label, period.Kind, null, period.End, next?.Label ?? "", next?.Start);
        return new CurrentPeriod("Unscheduled", null, null, next?.Start, next?.Label ?? "", next?.Start);
    }

    public PeriodKind PeriodForCategory(string category) =>
        _settings.Current.Schedule.CategoryPeriods.TryGetValue(category, out var k) ? k : PeriodKind.Free;
}
