using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace LifeHub.Ai;

/// <summary>What LifeHub said back, and the changes it suggests (each waits for you to confirm).</summary>
public sealed record AskReply(string Answer, List<AiSuggestion> Suggestions);

/// <summary>
/// "Ask LifeHub": answers questions about your tasks, schedule, kitchen, meals and renewals, and turns requests
/// ("add milk", "I'm out Thursday 2–4", "remember Sam is vegetarian") into suggestions. Nothing changes until you
/// confirm — the answer only ever says what LifeHub <em>would</em> do. Used by the app and the Telegram chat.
/// </summary>
public sealed class AskService
{
    private const int MaxQuestionChars = 1000;

    private readonly AiService _ai;
    private readonly TaskService _tasks;
    private readonly KitchenService _kitchen;
    private readonly RenewalService _renewals;
    private readonly MealService _meals;
    private readonly ScheduleService _schedule;
    private readonly CalendarService _calendar;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    private readonly ILogger<AskService> _log;

    public AskService(AiService ai, TaskService tasks, KitchenService kitchen, RenewalService renewals, MealService meals,
        ScheduleService schedule, CalendarService calendar, SettingsService settings, Clock clock, ILogger<AskService> log)
    {
        _ai = ai; _tasks = tasks; _kitchen = kitchen; _renewals = renewals; _meals = meals;
        _schedule = schedule; _calendar = calendar; _settings = settings; _clock = clock; _log = log;
    }

    public bool IsAvailable => _ai.IsAvailable;

    /// <summary>Asks the AI. <paramref name="origin"/> is "ask" (the app), "telegram" or "api".</summary>
    public async Task<AskReply> AskAsync(string text, string origin = "ask", CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("Type a question or something to do");
        if (text.Length > MaxQuestionChars) text = text[..MaxQuestionChars];
        if (!_ai.IsAvailable) throw new InvalidOperationException("Set up an AI in Settings → AI first.");

        var context = await ContextAsync(ct);
        var system = _ai.BuildSystemPrompt(origin) + "\n" + """
            ASK LIFEHUB
            The user is talking to you directly. Use the CONTEXT below (their lists, schedule and kitchen) to help.
            - A question: answer it in "answer" from the CONTEXT only. Be brief and friendly: a few short lines of plain
              text, no markdown tables or headings. If the CONTEXT doesn't say, say you don't know rather than guess.
            - A request to change something: propose the actions; they will confirm each one before anything happens.
              In "answer" say in one short sentence what you've suggested, e.g. "I can add milk and eggs to your shopping list."
              Never say it's done.
            - Both is fine. Things they say they've already done → "done" actions; never invent list items.
            - email_type is always "action_needed" here; summary = a few words about the request.
            """ + "\n" + context;

        var schema = _ai.BuildSchema();
        ((JsonObject)schema["properties"]!)["answer"] = new JsonObject { ["type"] = "string" };
        ((JsonArray)schema["required"]!).Add("answer");

        var result = await _ai.AnalyzeAsync("MESSAGE FROM THE USER:\n" + text, origin, ct, system, schema);
        foreach (var s in result.Suggestions) s.OriginText = text.Length > 300 ? text[..300] : text;
        // Saved for review, never applied automatically: the user confirms in the app or the chat.
        if (result.Suggestions.Count > 0) await _ai.Suggestions.UpsertManyAsync(result.Suggestions);

        var answer = result.Answer.Trim();
        if (answer.Length == 0)
            answer = result.Suggestions.Count > 0 ? "Here's what I can do — confirm what you want:" : "Sorry, I'm not sure how to help with that.";
        return new AskReply(answer, result.Suggestions);
    }

    /// <summary>A compact picture of the person's LifeHub for the AI (kept short for small local models).</summary>
    public async Task<string> ContextAsync(CancellationToken ct = default)
    {
        var inv = CultureInfo.InvariantCulture;
        var today = _clock.Today;
        var sb = new StringBuilder();
        sb.AppendLine("CONTEXT");

        // Schedule for the coming week: working hours, time off, out/kept free, calendar events.
        sb.AppendLine("Schedule, next 7 days:");
        List<CalendarEvent> events;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            events = await _calendar.GetEventsAsync(today, today.AddDays(6)).WaitAsync(timeout.Token);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Calendar unavailable for Ask"); events = new(); }
        for (var i = 0; i < 7; i++)
        {
            var d = today.AddDays(i);
            var dayEvents = events.Where(e => DateOnly.FromDateTime(e.Start) <= d && (e.AllDay ? DateOnly.FromDateTime(e.End) > d : DateOnly.FromDateTime(e.Start) == d)).ToList();
            var bits = new List<string> { _schedule.Summary(d, dayEvents) };
            bits.AddRange(_schedule.Periods(d, dayEvents).Where(p => p.Kind is PeriodKind.Out or PeriodKind.Reserved)
                .Select(p => $"{(p.Kind == PeriodKind.Out ? "out" : "keeping free")} {p.Start:HH:mm}-{p.End:HH:mm} {p.Label}"));
            bits.AddRange(dayEvents.Take(6).Select(e => e.AllDay ? $"all day: {e.Title}" : $"{e.Start:HH:mm} {e.Title}"));
            sb.AppendLine($"- {d.ToString("ddd yyyy-MM-dd", inv)}: {string.Join("; ", bits)}");
        }

        var open = _tasks.Ordered(_tasks.Open()).Take(40).ToList();
        sb.AppendLine($"Open tasks ({_tasks.Open().Count}):");
        foreach (var t in open)
        {
            var due = t.DueDate is { } dd ? $" due {dd:yyyy-MM-dd}{(t.DueTime is { } dt ? " " + dt.ToString("HH:mm", inv) : "")}" : "";
            var when = t.ScheduledStart is { } st ? $" planned {st:yyyy-MM-dd HH:mm}" : "";
            var pri = t.Priority is TaskPriority.High or TaskPriority.Urgent ? $" [{t.Priority.ToString().ToLower()}]" : "";
            sb.AppendLine($"- {t.Title} ({t.Category}){due}{when}{pri}");
        }

        var shopping = _kitchen.Shopping.Where(x => !x.Checked).Take(40).Select(x => x.Quantity > 1 ? $"{x.Name} x{x.Quantity:0.##}" : x.Name).ToList();
        sb.AppendLine($"Shopping list: {(shopping.Count == 0 ? "empty" : string.Join(", ", shopping))}");

        var soon = _kitchen.ExpiringWithin(3).Take(15).Select(p => p.Name).ToList();
        var stock = _kitchen.Pantry.All().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(60).ToList();
        sb.AppendLine($"In the kitchen: {(stock.Count == 0 ? "nothing recorded" : string.Join(", ", stock))}");
        if (soon.Count > 0) sb.AppendLine($"Use soon: {string.Join(", ", soon)}");

        var recipes = _meals.Recipes.All().ToDictionary(r => r.Id, r => r.Name);
        var meals = _meals.EntriesFor(today, today.AddDays(6))
            .Select(e => $"{e.Date.ToString("ddd", inv)} {e.Slot}: {(e.RecipeId is { } rid && recipes.TryGetValue(rid, out var n) ? n : e.Title)}").ToList();
        if (meals.Count > 0) sb.AppendLine($"Meal plan: {string.Join("; ", meals)}");

        var renewals = _renewals.Repo.Where(r => r.NextRenewal >= today && r.NextRenewal <= today.AddDays(60))
            .OrderBy(r => r.NextRenewal).Take(15)
            .Select(r => $"{r.Name} {r.NextRenewal:yyyy-MM-dd} {r.Cost.ToString("0.00", inv)} {r.Currency}").ToList();
        if (renewals.Count > 0) sb.AppendLine($"Renewals in the next 60 days: {string.Join("; ", renewals)}");
        return sb.ToString();
    }
}
