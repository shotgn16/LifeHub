using LifeHub.Modules;
using System.Globalization;

namespace LifeHub.Ai.Handlers;

/// <summary>"I'm out Thursday 2–4", "off next Friday", "keep Saturday morning free", "working Saturday 9–1".</summary>
public sealed class ScheduleActionHandler : IAiActionHandler, IUserOnlyAction
{
    private readonly ScheduleService _schedule;
    public ScheduleActionHandler(ScheduleService schedule) => _schedule = schedule;

    public string Type => "schedule";
    public string? Module => ModuleKeys.Schedule;
    public string Label => "Schedule change";
    public string Instructions =>
        "Only when the user talks about their own availability: time off, holiday or sick (category \"day_off\"), being out or away " +
        "(\"out\"), time to keep free (\"keep_free\"), or working extra or different hours (\"working\"). Examples: 'I'm out Thursday 2-4', " +
        "'off next Friday', 'on holiday 3rd to 7th March', 'keep Saturday morning free', 'working Saturday 9 till 1'. " +
        "title = what it's for in a few words ('Dentist', 'Holiday'; \"\" if not said). date = first day. end_date = last day when it's more " +
        "than one day. time = start time and duration_minutes = length, both omitted for a whole day. " +
        "Use \"event\" instead when they ask for something to go in their calendar.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        if (s.Date is null) throw new InvalidOperationException("No date given");
        var kind = (s.Category ?? "").ToLowerInvariant().Replace(' ', '_').Replace('-', '_') switch
        {
            "day_off" or "dayoff" or "holiday" or "off" => PlanKind.DayOff,
            "keep_free" or "reserved" or "busy" => PlanKind.Reserved,
            "working" or "work" => PlanKind.Working,
            _ => PlanKind.Out
        };
        var to = s.Extra.TryGetValue("end_date", out var e) && DateOnly.TryParseExact(e, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) && end >= s.Date
            ? end : s.Date.Value;
        TimeOnly? start = s.Time, finish = null;
        if (start is { } st) finish = st.AddMinutes(s.DurationMinutes is > 0 ? Math.Min(s.DurationMinutes.Value, 24 * 60 - 1) : 60);
        if (finish <= start) finish = new TimeOnly(23, 59);
        var title = s.Title.Trim();
        if (title.Equals(SchedulePlan.KindName(kind), StringComparison.OrdinalIgnoreCase)) title = "";
        var plan = await _schedule.AddPlanAsync(new SchedulePlan
        {
            Kind = kind, Label = title, From = s.Date.Value, To = to, Start = start, End = finish,
            Source = s.Origin is "telegram" ? "telegram" : "ask"
        });
        s.Extra["undo"] = $"plan:{plan.Id}";
        return $"Added to your schedule: {plan.Name}, {plan.When(CultureInfo.GetCultureInfo("en-GB"))}";
    }
}

/// <summary>"Remember that the bins go out on Tuesday night."</summary>
public sealed class MemoryActionHandler : IAiActionHandler, IUserOnlyAction
{
    private readonly MemoryService _memory;
    public MemoryActionHandler(MemoryService memory) => _memory = memory;

    public string Type => "memory";
    public string? Module => ModuleKeys.Memory;
    public string Label => "Remember";
    public string Instructions =>
        "Only when the user asks you to remember something, or tells you a lasting fact or preference about themselves or their household " +
        "that will matter later (routines, diets, allergies, people, regular arrangements), e.g. 'remember the bins go out Tuesday night', " +
        "'Sam is vegetarian'. title = the fact as one short sentence in the third person ('Bins go out on Tuesday night'). " +
        "Never for one-off tasks, events or shopping, and never for passwords, bank details or other secrets.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        if (LooksSecret(s.Title)) throw new InvalidOperationException("That looks like a password or account detail — LifeHub doesn't remember those");
        var fact = await _memory.AddAsync(s.Title, source: s.Origin is "telegram" ? "telegram" : "ask");
        s.Extra["undo"] = $"memory:{fact.Id}";
        return "Remembered";
    }

    private static bool LooksSecret(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"password|passcode|\bpin\b|sort code|account number|card number|cvv|\b\d{12,19}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

/// <summary>"Paid the council tax", "got the milk": ticks off a task or a shopping item that's already on a list.</summary>
public sealed class DoneActionHandler : IAiActionHandler, IUserOnlyAction
{
    private readonly TaskService _tasks;
    private readonly KitchenService _kitchen;
    public DoneActionHandler(TaskService tasks, KitchenService kitchen) { _tasks = tasks; _kitchen = kitchen; }

    public string Type => "done";
    public string Label => "Tick off";
    public string Instructions =>
        "Only when the user says they've already done something that is on their task list or bought something on their shopping list " +
        "('paid the council tax', 'got the milk'). category = \"task\" or \"shopping\". title = the item's name as it appears on the list. " +
        "One action per item.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var wantShopping = (s.Category ?? "").StartsWith("shop", StringComparison.OrdinalIgnoreCase);
        if (!wantShopping)
        {
            var task = Best(_tasks.Open(), t => t.Title, s.Title);
            if (task is not null)
            {
                await _tasks.CompleteAsync(task.Id);
                s.Extra["undo"] = $"done-task:{task.Id}";
                return $"Ticked off \"{task.Title}\"";
            }
        }
        var item = Best(_kitchen.Shopping.Where(x => !x.Checked), x => x.Name, s.Title);
        if (item is not null)
        {
            await _kitchen.ToggleAsync(item.Id);
            s.Extra["undo"] = $"done-shopping:{item.Id}";
            return $"Ticked \"{item.Name}\" off the shopping list";
        }
        throw new InvalidOperationException($"Couldn't find \"{s.Title}\" on your lists");
    }

    /// <summary>The list entry that best matches what was said: the same words, or one containing the other.</summary>
    private static T? Best<T>(IEnumerable<T> items, Func<T, string> name, string said) where T : class
    {
        var want = TaskService.Normalize(said);
        if (want.Length == 0) return null;
        var list = items.ToList();
        return list.FirstOrDefault(i => TaskService.Normalize(name(i)) == want)
            ?? list.FirstOrDefault(i => TaskService.IsSameIssue(name(i), said))
            ?? list.Where(i => TaskService.Normalize(name(i)).Length > 2).Where(i => TaskService.Normalize(name(i)).Contains(want) || want.Contains(TaskService.Normalize(name(i))))
                   .OrderBy(i => Math.Abs(name(i).Length - said.Length)).FirstOrDefault();
    }
}
