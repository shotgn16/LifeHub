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

/// <summary>
/// Works out what kind of time it is (work / free / out / reserved) from your usual week, the one-off plans
/// you've added in Schedule (holidays, time out, reserved time, extra work) and calendar events.
/// </summary>
public sealed class ScheduleService
{
    private readonly SettingsService _settings;
    private readonly CalendarService _calendar;
    private readonly Clock _clock;
    private readonly IRepository<SchedulePlan> _plans;

    public ScheduleService(SettingsService settings, CalendarService calendar, Clock clock, IRepository<SchedulePlan> plans)
    {
        _settings = settings; _calendar = calendar; _clock = clock; _plans = plans;
    }

    public IRepository<SchedulePlan> Plans => _plans;

    /// <summary>One-off plans covering a day.</summary>
    public List<SchedulePlan> PlansOn(DateOnly date) => _plans.Where(p => p.Covers(date)).OrderBy(p => p.Start ?? TimeOnly.MinValue).ToList();

    /// <summary>Plans from today on, soonest first.</summary>
    public List<SchedulePlan> Upcoming(int days = 365)
    {
        var today = _clock.Today;
        var until = today.AddDays(days);
        return _plans.Where(p => (p.To < p.From ? p.From : p.To) >= today && p.From <= until)
            .OrderBy(p => p.From).ThenBy(p => p.Start ?? TimeOnly.MinValue).ToList();
    }

    public async Task<SchedulePlan> AddPlanAsync(SchedulePlan plan)
    {
        if (plan.To < plan.From) plan.To = plan.From;
        if (plan.Start is { } s && plan.End is { } e && e <= s) throw new InvalidOperationException("The end time needs to be after the start time");
        if (plan.Kind != PlanKind.DayOff && plan.Kind != PlanKind.Working && plan.AllDay && plan.To.DayNumber - plan.From.DayNumber > 60)
            throw new InvalidOperationException("That's more than two months — add it as a day off instead");
        plan.Label = plan.Label.Trim();
        // The same thing twice (a double tap, or saying it again) is kept once.
        var same = _plans.Where(p => p.Id != plan.Id && p.Kind == plan.Kind && p.From == plan.From && p.To == plan.To &&
                                     p.Start == plan.Start && p.End == plan.End && string.Equals(p.Label, plan.Label, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (same is not null) return same;
        await _plans.UpsertAsync(plan);
        return plan;
    }

    public Task DeletePlanAsync(Guid id) => _plans.DeleteAsync(id);

    /// <summary>A day off: an all-day "day off" plan, or an all-day calendar event with a day-off word (holiday, annual leave…).</summary>
    public bool IsDayOff(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        if (PlansOn(date).Any(p => p.Kind == PlanKind.DayOff && p.AllDay)) return true;
        var words = _settings.Current.Schedule.DayOffKeywords;
        return events.Any(e => e.AllDay && DateOnly.FromDateTime(e.Start) <= date && DateOnly.FromDateTime(e.End) > date &&
                               words.Any(w => e.Title.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The periods for a day: your usual week, with work turned into free time on days off and one-off plans laid on top.</summary>
    public List<DaySegment> Periods(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        var sched = _settings.Current.Schedule;
        var plans = PlansOn(date);
        var dayOff = IsDayOff(date, events);
        var offName = plans.FirstOrDefault(p => p.Kind == PlanKind.DayOff && p.AllDay)?.Name ?? "Day off";
        var list = sched.Blocks
            .Where(b => b.Days.Contains(date.DayOfWeek))
            .Select(b =>
            {
                var start = date.ToDateTime(b.Start);
                var end = b.End <= b.Start ? date.AddDays(1).ToDateTime(b.End) : date.ToDateTime(b.End);
                var kind = dayOff && b.Kind == PeriodKind.Work ? PeriodKind.Free : b.Kind;
                var label = dayOff && b.Kind == PeriodKind.Work ? offName : (string.IsNullOrWhiteSpace(b.Label) ? b.Kind.ToString() : b.Label);
                return new DaySegment { Start = start, End = end, Kind = kind, Label = label };
            })
            .OrderBy(s => s.Start).ToList();

        // The waking day, for all-day plans: the usual periods' span, or a sensible default.
        var dayStart = list.Count > 0 ? list.Min(p => p.Start) : date.ToDateTime(new TimeOnly(8, 0));
        var dayEnd = list.Count > 0 ? list.Max(p => p.End) : date.ToDateTime(new TimeOnly(22, 0));
        var usualWork = (sched.Usual?.Days.FirstOrDefault(d => d.Working)) ?? new UsualDay();

        foreach (var p in plans)
        {
            switch (p.Kind)
            {
                case PlanKind.DayOff when p.AllDay:
                    break;   // handled above
                case PlanKind.DayOff:
                    list = Overlay(list, Seg(date, p.Start!.Value, p.End!.Value, PeriodKind.Free, p.Name), onlyOver: PeriodKind.Work);
                    break;
                case PlanKind.Working:
                    var (ws, we) = p.AllDay ? (usualWork.WorkStart, usualWork.WorkEnd) : (p.Start!.Value, p.End!.Value);
                    list = Overlay(list, Seg(date, ws, we, PeriodKind.Work, p.Name));
                    break;
                default:
                    var kind = p.Kind == PlanKind.Out ? PeriodKind.Out : PeriodKind.Reserved;
                    var seg = p.AllDay ? new DaySegment { Start = dayStart, End = dayEnd, Kind = kind, Label = p.Name }
                                       : Seg(date, p.Start!.Value, p.End!.Value, kind, p.Name);
                    list = Overlay(list, seg);
                    break;
            }
        }
        return list;
    }

    private static DaySegment Seg(DateOnly date, TimeOnly start, TimeOnly end, PeriodKind kind, string label) => new()
    {
        Start = date.ToDateTime(start),
        End = end <= start ? date.AddDays(1).ToDateTime(end) : date.ToDateTime(end),
        Kind = kind, Label = label
    };

    /// <summary>Lays <paramref name="seg"/> over the day, cutting what was there (only periods of <paramref name="onlyOver"/> when given).</summary>
    private static List<DaySegment> Overlay(List<DaySegment> list, DaySegment seg, PeriodKind? onlyOver = null)
    {
        var result = new List<DaySegment>();
        foreach (var p in list)
        {
            if (p.End <= seg.Start || p.Start >= seg.End || (onlyOver is { } k && p.Kind != k)) { result.Add(p); continue; }
            if (p.Start < seg.Start) result.Add(new DaySegment { Start = p.Start, End = seg.Start, Kind = p.Kind, Label = p.Label });
            if (p.End > seg.End) result.Add(new DaySegment { Start = seg.End, End = p.End, Kind = p.Kind, Label = p.Label });
            if (onlyOver is not null)
                result.Add(new DaySegment { Start = p.Start > seg.Start ? p.Start : seg.Start, End = p.End < seg.End ? p.End : seg.End, Kind = seg.Kind, Label = seg.Label });
        }
        if (onlyOver is null) result.Add(seg);
        return result.Where(p => p.End > p.Start).OrderBy(p => p.Start).ToList();
    }

    /// <summary>A short line for a day: "Working 09:00–17:30", "Holiday", "Day off" (week view, Ask LifeHub).</summary>
    public string Summary(DateOnly date, IEnumerable<CalendarEvent> events)
    {
        var periods = Periods(date, events);
        var work = periods.Where(p => p.Kind == PeriodKind.Work).ToList();
        if (work.Count > 0) return $"Working {work.Min(p => p.Start):HH:mm}–{work.Max(p => p.End):HH:mm}";
        if (IsDayOff(date, events)) return PlansOn(date).FirstOrDefault(p => p.Kind == PlanKind.DayOff && p.AllDay)?.Name ?? "Day off";
        return "Not working";
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
