namespace LifeHub.Services;

public sealed record PlannedTask(Guid TaskId, string Title, string Category, DateTime Start, DateTime End, PeriodKind Kind);

public sealed record DayPlan(DateOnly Date, List<PlannedTask> Planned, List<TaskItem> Unplanned, List<CalendarEvent> Events, List<DaySegment> Periods);

/// <summary>
/// "Plan my day": slots open tasks into the free gaps of matching periods
/// (Work tasks into Work time, everything else into Free time by default),
/// around calendar events and anything already scheduled.
/// </summary>
public sealed class PlannerService
{
    private readonly TaskService _tasks;
    private readonly ScheduleService _schedule;
    private readonly CalendarService _calendar;
    private readonly SettingsService _settings;
    private readonly Clock _clock;

    public PlannerService(TaskService tasks, ScheduleService schedule, CalendarService calendar, SettingsService settings, Clock clock)
    {
        _tasks = tasks; _schedule = schedule; _calendar = calendar; _settings = settings; _clock = clock;
    }

    public async Task<DayPlan> ProposeAsync(DateOnly date, int lookaheadDays = 7, int? maxTasks = null)
    {
        var s = _settings.Current.Schedule;
        var (periods, events) = await _schedule.DayAsync(date);
        var now = _clock.Now;
        var earliest = date == DateOnly.FromDateTime(now) ? RoundUp(now, 5) : date.ToDateTime(TimeOnly.MinValue);

        var open = _tasks.Open();
        var fixedToday = open.Where(t => t.ScheduledStart is { } st && DateOnly.FromDateTime(st) == date).ToList();

        // Busy intervals: timed events + tasks already given a time today.
        var busy = events.Where(e => !e.AllDay).Select(e => (e.Start, e.End))
            .Concat(fixedToday.Select(t => (t.ScheduledStart!.Value, t.ScheduledStart!.Value.AddMinutes(t.EstimatedMinutes ?? s.DefaultTaskMinutes))))
            .ToList();

        // Free gaps per period kind.
        var gaps = new List<(DateTime Start, DateTime End, PeriodKind Kind)>();
        foreach (var p in periods.Where(p => p.Kind is not (PeriodKind.Sleep or PeriodKind.Commute)))
        {
            var pieces = new List<(DateTime, DateTime)> { (Max(p.Start, earliest), p.End) };
            foreach (var (bs, be) in busy)
                pieces = pieces.SelectMany(g => Subtract(g, (bs.AddMinutes(-s.BufferMinutes), be.AddMinutes(s.BufferMinutes)))).ToList();
            gaps.AddRange(pieces.Where(g => g.Item2 > g.Item1).Select(g => (g.Item1, g.Item2, p.Kind!.Value)));
        }

        var horizon = date.AddDays(lookaheadDays);
        var candidates = open
            // Not yet planned, or planned in the past and still not done.
            .Where(t => t.ScheduledStart is null || t.ScheduledStart.Value < now)
            .Where(t => t.State != TaskState.Waiting)
            .Where(t => t.DueDate is null || t.DueDate <= horizon)
            .OrderBy(t => t.DueDate is { } d && d < date ? 0 : 1)        // overdue first
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
            .ThenBy(t => t.CreatedUtc)
            .ToList();

        var planned = new List<PlannedTask>();
        var unplanned = new List<TaskItem>();
        foreach (var t in candidates)
        {
            if (maxTasks is { } max && planned.Count >= max)
            {
                if (t.DueDate is { } due && due <= date) unplanned.Add(t);
                continue;
            }
            var kind = _schedule.PeriodForCategory(t.Category);
            var minutes = t.EstimatedMinutes ?? s.DefaultTaskMinutes;
            var idx = gaps.FindIndex(g => g.Kind == kind && (g.End - g.Start).TotalMinutes >= minutes &&
                                          (t.DueAt is null || g.Start.AddMinutes(minutes) <= t.DueAt || t.DueDate < date));
            if (idx < 0)
            {
                // Only report as "didn't fit" if it actually needed doing by today.
                if (t.DueDate is { } d && d <= date) unplanned.Add(t);
                continue;
            }
            var g = gaps[idx];
            var start = g.Start;
            var end = start.AddMinutes(minutes);
            planned.Add(new PlannedTask(t.Id, t.Title, t.Category, start, end, kind));
            gaps[idx] = (end.AddMinutes(s.BufferMinutes), g.End, g.Kind);
        }

        return new DayPlan(date, planned.OrderBy(p => p.Start).ToList(), unplanned, events, periods);
    }

    /// <summary>Saves the proposed times onto the tasks, optionally creating calendar events.</summary>
    public async Task<int> ApplyAsync(DayPlan plan, bool addToCalendar)
    {
        var count = 0;
        foreach (var p in plan.Planned)
        {
            var t = _tasks.Repo.Get(p.TaskId);
            if (t is null) continue;
            t.ScheduledStart = p.Start;
            if (addToCalendar && _calendar.CanCreateEvents)
            {
                await _calendar.CreateEventAsync(t.Title, p.Start, p.End, $"LifeHub task ({t.Category})\n{t.Notes}");
                t.CalendarEventRef = "created";
            }
            await _tasks.SaveAsync(t);
            count++;
        }
        return count;
    }

    private static DateTime RoundUp(DateTime d, int minutes)
    {
        var m = (int)Math.Ceiling(d.TimeOfDay.TotalMinutes / minutes) * minutes;
        return d.Date.AddMinutes(m);
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static IEnumerable<(DateTime, DateTime)> Subtract((DateTime Start, DateTime End) gap, (DateTime Start, DateTime End) busy)
    {
        if (busy.End <= gap.Start || busy.Start >= gap.End) { yield return gap; yield break; }
        if (busy.Start > gap.Start) yield return (gap.Start, busy.Start);
        if (busy.End < gap.End) yield return (busy.End, gap.End);
    }
}
