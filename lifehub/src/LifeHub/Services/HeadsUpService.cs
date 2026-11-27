using System.Globalization;
using System.Text.RegularExpressions;

namespace LifeHub.Services;

/// <summary>An upcoming calendar event and what LifeHub remembers that's relevant to it.</summary>
public sealed record HeadsUp(CalendarEvent Event, DateOnly Date, int DaysAway, List<MemoryFact> Facts)
{
    public bool AboutFood => Facts.Any(f => HeadsUpService.IsDiet(f.Text));
}

/// <summary>
/// Heads-ups: every morning LifeHub looks at the coming days in your calendars, and when an event mentions someone or
/// something it remembers ("Sam over" + "Sam is vegan"), it reminds you on the days you chose leading up to it — and, for
/// dietary needs, suggests meals for that day in Meals (you still confirm them). The matching is plain word matching on
/// names, so it works without an AI and does nothing surprising.
/// </summary>
public sealed class HeadsUpService
{
    private static readonly Regex Words = new(@"[\p{L}][\p{L}\p{N}'’\-]*", RegexOptions.Compiled);
    private static readonly Regex Diet = new(
        @"vegan|vegetarian|pescatarian|plant[- ]based|allerg|intoleran|gluten|coeliac|celiac|dairy|lactose|halal|kosher|diabet|keto|" +
        @"nut[- ]free|(doesn['’]?t|does not|don['’]?t|do not|can['’]?t|cannot|won['’]?t|never) eats?|no (meat|pork|fish|beef|nuts|eggs|shellfish)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Capitalised words that aren't names, so "The bins go out on Tuesday" doesn't match every Tuesday event.</summary>
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "if", "on", "in", "at", "to", "for", "of", "with", "from", "by", "every", "each", "always",
        "never", "usually", "often", "sometimes", "i", "i'm", "im", "my", "me", "we", "our", "us", "you", "your", "he", "she", "they",
        "his", "her", "their", "it", "its", "this", "that", "these", "those", "is", "are", "was", "be", "has", "have", "had", "do", "does",
        "don't", "doesn't", "not", "no", "yes", "remember", "lifehub", "mum", "mom", "dad", "monday", "tuesday", "wednesday", "thursday",
        "friday", "saturday", "sunday", "mon", "tue", "tues", "wed", "thu", "thur", "thurs", "fri", "sat", "sun", "january", "february",
        "march", "april", "may", "june", "july", "august", "september", "october", "november", "december", "today", "tomorrow",
        "morning", "evening", "night", "week", "weekend", "month", "year", "christmas", "easter", "bank", "holiday", "work", "home"
    };

    private readonly MemoryService _memory;
    private readonly CalendarService _calendar;
    private readonly SettingsService _settings;
    private readonly NotificationService _notices;
    private readonly Clock _clock;
    private readonly IServiceProvider _sp;
    private readonly ILogger<HeadsUpService> _log;

    public HeadsUpService(MemoryService memory, CalendarService calendar, SettingsService settings, NotificationService notices,
        Clock clock, IServiceProvider sp, ILogger<HeadsUpService> log)
    {
        _memory = memory; _calendar = calendar; _settings = settings; _notices = notices; _clock = clock; _sp = sp; _log = log;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastRun = new();

    /// <summary>Memory changed: check again on the next run instead of waiting half an hour.</summary>
    public void Invalidate() => _lastRun.Clear();

    public static bool IsDiet(string text) => Diet.IsMatch(text ?? "");

    /// <summary>The names a fact is about: capitalised words that aren't ordinary words ("Sam", "Grandma", "Max").</summary>
    public static HashSet<string> NamesIn(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Words.Matches(text ?? ""))
        {
            var w = Base(m.Value);
            if (w.Length >= 2 && char.IsUpper(m.Value[0]) && !NotNames.Contains(w)) set.Add(w);
        }
        return set;
    }

    /// <summary>The facts an event title is about: those naming someone or something the title mentions.</summary>
    public static List<MemoryFact> Match(IEnumerable<MemoryFact> facts, string title)
    {
        var words = Words.Matches(title ?? "").Select(m => Base(m.Value)).Where(w => w.Length >= 2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (words.Count == 0) return new();
        return facts.Where(f => NamesIn(f.Text).Any(words.Contains)).ToList();
    }

    /// <summary>"Sam's" → "Sam".</summary>
    private static string Base(string word)
    {
        var w = word.Trim('\'', '’', '-');
        if (w.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || w.EndsWith("’s", StringComparison.OrdinalIgnoreCase)) w = w[..^2];
        return w;
    }

    /// <summary>What's coming up for the current person, soonest first (the Memory page shows this).</summary>
    public async Task<List<HeadsUp>> UpcomingAsync(int? days = null, CancellationToken ct = default)
    {
        if (!_memory.IsOn) return new();
        var facts = _memory.All();
        if (facts.Count == 0) return new();
        var today = _clock.Today;
        var ahead = Math.Clamp(days ?? _settings.Household.Memory.LookAheadDays, 1, 31);
        List<CalendarEvent> events;
        try { events = await _calendar.GetEventsAsync(today, today.AddDays(ahead)).WaitAsync(TimeSpan.FromSeconds(20), ct); }
        catch (Exception ex) { _log.LogDebug(ex, "Calendar unavailable for heads-ups"); return new(); }
        var list = new List<HeadsUp>();
        foreach (var e in events)
        {
            var date = DateOnly.FromDateTime(e.Start);
            if (date < today) continue;
            var matched = Match(facts, e.Title);
            if (matched.Count > 0) list.Add(new HeadsUp(e, date, date.DayNumber - today.DayNumber, matched));
        }
        return list.OrderBy(h => h.Event.Start).ToList();
    }

    /// <summary>Background: sends today's heads-ups for the current person (each one once).</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        var cfg = _settings.Household.Memory;
        if (TimeOnly.FromDateTime(_clock.Now) < cfg.HeadsUpTime) return;
        // Every half hour is plenty: picks up events added during the day without asking the calendar every minute.
        var who = UserScope.UserId ?? Guid.Empty;
        if (_lastRun.TryGetValue(who, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(30)) return;
        _lastRun[who] = DateTime.UtcNow;
        var remindOn = cfg.RemindDaysBefore.ToHashSet();
        if (remindOn.Count == 0) return;
        foreach (var h in await UpcomingAsync(ct: ct))
        {
            if (!remindOn.Contains(h.DaysAway)) continue;
            var eventKey = $"headsup:{h.Date:yyyyMMdd}:{TaskService.Normalize(h.Event.Title)}:";
            var key = eventKey + h.DaysAway;
            if (_notices.Repo.Where(n => n.DedupeKey == key).Count > 0) continue;
            var first = _notices.Repo.Where(n => n.DedupeKey.StartsWith(eventKey)).Count == 0;

            var extra = "";
            var link = "memory";
            if (first && h.AboutFood && h.DaysAway >= 1 && cfg.SuggestMeals)
            {
                try
                {
                    var made = await SuggestMealsAsync(h, ct);
                    if (made > 0) { extra = $"\nI've suggested {(made == 1 ? "a meal" : "meals")} for that day in Meals."; link = "meals"; }
                }
                catch (Exception ex) { _log.LogInformation("Meal ideas for {Title} failed: {Error}", h.Event.Title, ex.Message); }
            }
            var title = $"{h.Event.Title} — {When(h)}";
            var message = "Remember: " + string.Join(" · ", h.Facts.Take(4).Select(f => f.Text.TrimEnd('.'))) + "." + extra;
            await _notices.NotifyAsync(title, message, NoticeLevel.Info, link, key, tag: "headsup", category: NoticeCategories.HeadsUp);
        }
    }

    /// <summary>Asks the meal planner for meals that suit the guests on that day (they wait in Meals for you to apply).</summary>
    public async Task<int> SuggestMealsAsync(HeadsUp h, CancellationToken ct = default)
    {
        var meals = _sp.GetRequiredService<MealService>();
        var modules = _sp.GetService<LifeHub.Modules.ModuleRegistry>();
        if (!meals.AiAvailable || modules?.IsOn("meals") == false) return 0;
        var note = $"\"{h.Event.Title}\"{(h.Event.AllDay ? "" : " at " + h.Event.Start.ToString("HH:mm", CultureInfo.InvariantCulture))} — " +
                   string.Join("; ", h.Facts.Where(f => IsDiet(f.Text)).Select(f => f.Text));
        var made = await meals.SuggestForDayAsync(h.Date, note, ct);
        return made.Count;
    }

    public static string When(HeadsUp h) => h.DaysAway switch
    {
        0 => h.Event.AllDay ? "today" : $"today at {h.Event.Start:HH:mm}",
        1 => h.Event.AllDay ? "tomorrow" : $"tomorrow at {h.Event.Start:HH:mm}",
        < 7 => $"{h.Date.ToString("dddd", CultureInfo.GetCultureInfo("en-GB"))} (in {h.DaysAway} days)",
        _ => $"{h.Date.ToString("ddd d MMM", CultureInfo.GetCultureInfo("en-GB"))} (in {h.DaysAway} days)"
    };

    /// <summary>Notes for the meal planner: which planned days have guests with dietary needs.</summary>
    public async Task<Dictionary<DateOnly, string>> MealNotesAsync(DateOnly from, DateOnly to)
    {
        var result = new Dictionary<DateOnly, string>();
        if (!_memory.IsOn) return result;
        var facts = _memory.All().Where(f => IsDiet(f.Text)).ToList();
        if (facts.Count == 0) return result;
        List<CalendarEvent> events;
        try { events = await _calendar.GetEventsAsync(from, to); } catch { return result; }
        foreach (var g in events.GroupBy(e => DateOnly.FromDateTime(e.Start)))
        {
            var notes = g.Select(e => (e, Match(facts, e.Title))).Where(x => x.Item2.Count > 0)
                .Select(x => $"\"{x.e.Title}\": {string.Join("; ", x.Item2.Select(f => f.Text))}").ToList();
            if (notes.Count > 0) result[g.Key] = string.Join(" | ", notes);
        }
        return result;
    }
}
