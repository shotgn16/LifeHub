using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeHub.Integrations.Email;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Ai;

public sealed record AnalysisResult(string Summary, string Importance, List<AiSuggestion> Suggestions, string Category = "");

/// <summary>
/// Turns text (an email or a quick-capture note) into reviewed or auto-applied actions using
/// a local Ollama model with a JSON-schema constrained response.
/// </summary>
public sealed class AiService
{
    private readonly OllamaClient _ollama;
    private readonly IEnumerable<IAiActionHandler> _handlers;
    private readonly IRepository<AiSuggestion> _suggestions;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    private readonly IServiceProvider _sp;
    private readonly ILogger<AiService> _log;

    public AiService(OllamaClient ollama, IEnumerable<IAiActionHandler> handlers, IRepository<AiSuggestion> suggestions,
        SettingsService settings, Clock clock, IServiceProvider sp, ILogger<AiService> log)
    {
        _ollama = ollama; _handlers = handlers; _suggestions = suggestions; _settings = settings; _clock = clock; _sp = sp; _log = log;
    }

    public IRepository<AiSuggestion> Suggestions => _suggestions;
    public IEnumerable<IAiActionHandler> Handlers => _handlers;
    public bool IsAvailable => _ollama.IsConfigured;

    public int PendingCount => _suggestions.Where(s => s.Status == SuggestionStatus.Pending).Count;

    // ---------- Prompt & schema ----------

    public string BuildSystemPrompt()
    {
        var s = _settings.Current;
        var now = _clock.Now;
        var sb = new StringBuilder();
        sb.AppendLine($"You are the personal organiser assistant inside LifeHub for the {s.General.HouseholdName} household.");
        sb.AppendLine($"Right now it is {now.ToString("dddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture)} ({_clock.Zone.Id}). ISO date today: {now:yyyy-MM-dd}.");
        sb.AppendLine("Read the input and propose concrete actions for the user. Available action types:");
        foreach (var h in _handlers) sb.AppendLine($"- \"{h.Type}\": {h.Instructions}");
        sb.AppendLine();
        sb.AppendLine($"Task categories: {string.Join(", ", s.General.TaskCategories)}.");
        sb.AppendLine($"Food categories: {string.Join(", ", s.General.FoodCategories)}.");
        sb.AppendLine($"Default currency: {s.General.Currency}.");
        sb.AppendLine("""
            First decide email_type (for notes typed by the user use "action_needed"):
              action_needed = someone (a person, employer, school, council…) asks the user to do or reply to something
              personal / work = ordinary correspondence; bill = invoice or payment due for something the user already has;
              receipt = confirmation of something the user ALREADY bought or ordered; delivery = parcel/shipping updates;
              booking = confirmed appointment, reservation, flight, hotel or ticket the user made;
              account = security/sign-in/password/account-change notices; notification = automated alerts (social media, apps);
              promotion = advertising: sales, offers, discounts, "new arrivals", new games/products/films, holiday or travel
                deals, "you might like", vouchers, price drops, launches, anything trying to get the user to buy something
                they have NOT already bought; newsletter = marketing content/news digests from brands or mailing lists.
              Telltale promotion signs: unsubscribe links, "shop now", "book now", "% off", "limited time", "deal", "sale",
              "pre-order", "don't miss out", sent from a brand's marketing/noreply address to a mailing list.
            Rules:
            - Only propose actions clearly supported by the text. Never invent dates, amounts or people.
            - For promotion or newsletter emails use ONLY the "promotion" action (at most one, describing the offer).
              NEVER create shopping, task, event, renewal or pantry actions from adverts — the user hasn't decided to buy.
            - "shopping" is ONLY for a note typed by the user that says they need to buy something. NEVER create
              "shopping" actions from emails of any kind (receipts, deliveries, adverts, newsletters, reminders).
            - Receipts, deliveries and notifications that need nothing doing → no actions
              (except "renewal" for a service the user pays for, or "pantry" for groceries that arrived).
            - Convert relative dates ("tomorrow", "next Tuesday", "end of the month") to YYYY-MM-DD using today's date.
              Use "" when there is no date. Times are 24h HH:mm or "".
            - Prefer one good action over several weak ones. Don't create both a task and an event for the same thing
              unless preparation is genuinely needed before the event.
            - confidence: 0.0–1.0, how sure you are the user would want this action exactly as written.
            - summary: one short sentence describing the input. importance: low | normal | high.
            """);
        if (!string.IsNullOrWhiteSpace(s.Ollama.ExtraInstructions))
        {
            sb.AppendLine("Additional rules from the user:");
            sb.AppendLine(s.Ollama.ExtraInstructions);
        }
        return sb.ToString();
    }

    public JsonObject BuildSchema()
    {
        JsonObject Str() => new() { ["type"] = "string" };
        var types = new JsonArray(_handlers.Select(h => (JsonNode)JsonValue.Create(h.Type)!).ToArray());
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["summary"] = Str(),
                ["email_type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(EmailCategories.All.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()) },
                ["importance"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("low", "normal", "high") },
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["type"] = new JsonObject { ["type"] = "string", ["enum"] = types },
                            ["title"] = Str(),
                            ["description"] = Str(),
                            ["date"] = Str(),
                            ["time"] = Str(),
                            ["duration_minutes"] = new JsonObject { ["type"] = "integer" },
                            ["category"] = Str(),
                            ["priority"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("low", "normal", "high", "urgent") },
                            ["amount"] = new JsonObject { ["type"] = "number" },
                            ["currency"] = Str(),
                            ["confidence"] = new JsonObject { ["type"] = "number" }
                        },
                        ["required"] = new JsonArray("type", "title", "confidence")
                    }
                }
            },
            ["required"] = new JsonArray("summary", "email_type", "importance", "actions")
        };
    }

    // ---------- Analysis ----------

    public async Task<AnalysisResult> AnalyzeAsync(string input, string origin, CancellationToken ct = default)
    {
        var json = await _ollama.ChatJsonAsync(BuildSystemPrompt(), input, BuildSchema(), ct);
        var summary = Str(json, "summary") ?? "";
        var importance = Str(json, "importance") ?? "normal";
        var category = (Str(json, "email_type") ?? "").ToLowerInvariant();
        if (!EmailCategories.All.Contains(category)) category = EmailCategories.Other;
        var list = new List<AiSuggestion>();
        if (json.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in actions.EnumerateArray())
            {
                var type = (Str(a, "type") ?? "").ToLowerInvariant();
                var title = Str(a, "title");
                if (string.IsNullOrWhiteSpace(title) || _handlers.All(h => h.Type != type)) continue;
                list.Add(new AiSuggestion
                {
                    Type = type,
                    Title = title.Trim(),
                    Description = Str(a, "description") ?? "",
                    Date = DateOnly.TryParseExact(Str(a, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                    Time = TimeOnly.TryParseExact(Str(a, "time"), new[] { "HH:mm", "H:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null,
                    DurationMinutes = Num(a, "duration_minutes") is { } dm && dm > 0 ? (int)dm : null,
                    Category = Str(a, "category"),
                    Priority = Str(a, "priority"),
                    Amount = Num(a, "amount") is { } am ? (decimal)am : null,
                    Currency = Str(a, "currency"),
                    Confidence = Math.Clamp(Num(a, "confidence") ?? 0.5, 0, 1),
                    Origin = origin
                });
            }
        }
        // Throw away model nonsense: boilerplate, explanations instead of titles, very low confidence.
        list.RemoveAll(a => IsJunkTitle(a.Title, a.Type) || a.Confidence < 0.35);
        foreach (var a in list)
            if (a.Currency is { } c && !(c.Length == 3 && c.All(char.IsLetter))) a.Currency = null;

        if (origin == "email")
        {
            // The shopping list only changes when you add things (typed, scanned or the linked HA list).
            list.RemoveAll(a => a.Type == "shopping");
            // Stock only from grocery deliveries/receipts; renewals only from bills/receipts/account mail.
            if (category is not (EmailCategories.Receipt or EmailCategories.Delivery)) list.RemoveAll(a => a.Type == "pantry");
            if (category is not (EmailCategories.Bill or EmailCategories.Receipt or EmailCategories.Account or EmailCategories.ActionNeeded or EmailCategories.Notification))
                list.RemoveAll(a => a.Type == "renewal");
        }

        // Safety net for small models: adverts never become shopping/tasks/events/renewals/stock.
        if (origin == "email" && EmailCategories.IsMarketing(category))
        {
            var promo = list.FirstOrDefault(a => a.Type == "promotion");
            if (promo is null && list.Count > 0 && _handlers.Any(h => h.Type == "promotion"))
            {
                var first = list[0];
                promo = new AiSuggestion
                {
                    Type = "promotion", Title = first.Title, Description = first.Description, Date = first.Date,
                    Amount = first.Amount, Currency = first.Currency, Confidence = 0.9, Origin = origin
                };
            }
            list = promo is null ? new List<AiSuggestion>() : new List<AiSuggestion> { promo };
            importance = "low";
        }
        else
        {
            // And promotions only come from marketing emails.
            list.RemoveAll(a => a.Type == "promotion" && origin == "email");
        }
        return new AnalysisResult(summary, importance, list, category);
    }

    public Task<AnalysisResult> AnalyzeEmailAsync(EmailMessage m, CancellationToken ct = default)
    {
        var max = _settings.Current.Ollama.MaxEmailChars;
        var bulk = m.IsBulk ? "\nSent via mailing list: yes (has an unsubscribe header — very likely marketing unless it's a receipt, delivery, bill or booking)" : "";
        var input = $"EMAIL\nFrom: {m.From}\nReceived: {_clock.ToLocal(m.ReceivedUtc):yyyy-MM-dd HH:mm}\nSubject: {m.Subject}{bulk}\n\n{EmailText.ForModel(m.Body, max)}";
        return AnalyzeAsync(input, "email", ct);
    }

    /// <summary>Quick capture: free text typed (or sent from HA voice) → suggestions saved and auto-applied per settings.</summary>
    public async Task<List<AiSuggestion>> CaptureAsync(string text, string origin = "capture", CancellationToken ct = default)
    {
        var result = await AnalyzeAsync("NOTE FROM THE USER (they want these things done/recorded):\n" + text, origin, ct);
        foreach (var s in result.Suggestions) s.OriginText = text.Length > 300 ? text[..300] : text;
        await SaveAndAutoApplyAsync(result.Suggestions);
        return result.Suggestions.Select(s => _suggestions.Get(s.Id) ?? s).ToList();
    }

    // ---------- Persist / apply ----------

    public async Task SaveAndAutoApplyAsync(List<AiSuggestion> suggestions, bool forceApply = false)
    {
        if (suggestions.Count == 0) return;
        await _suggestions.UpsertManyAsync(suggestions);
        var o = _settings.Current.Ollama;
        var applied = new List<AiSuggestion>();
        foreach (var s in suggestions)
        {
            if (forceApply || (o.AutoApplyTypes.Contains(s.Type) && s.Confidence >= o.AutoApplyMinConfidence))
            {
                var r = await ApplyAsync(s.Id, auto: true);
                if (r?.Status == SuggestionStatus.Applied) applied.Add(r);
            }
        }
        // Let you know what was done on your behalf from email (in-app only, with undo in the AI Inbox).
        if (applied.Count > 0 && applied[0].Origin == "email")
        {
            var what = string.Join(", ", applied.Select(a => $"{a.Type} \"{a.Title}\""));
            await _sp.GetRequiredService<NotificationService>().NotifyAsync(
                $"Added from email: {applied.Count} item(s)", $"{what} — from {applied[0].OriginText}. Undo in AI Inbox → History.",
                NoticeLevel.Info, "inbox", $"auto:{applied[0].Id}", push: false);
        }
    }

    /// <summary>True when an applied suggestion can be reversed (including renewals created before 1.2).</summary>
    public static bool CanUndo(AiSuggestion s) => s.Status == SuggestionStatus.Applied &&
        (s.Extra.ContainsKey("undo") || (s.Type == "renewal" && s.Result.StartsWith("New renewal tracked:")));

    /// <summary>Applied suggestions that can still be undone, grouped by type (for the clean-up panel).</summary>
    public Dictionary<string, int> UndoableCounts(bool autoOnly) =>
        _suggestions.Where(s => CanUndo(s) && (!autoOnly || s.AutoApplied || s.Origin == "email"))
            .GroupBy(s => s.Type).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Undoes every applied suggestion of one type that came from email. Returns how many.</summary>
    public async Task<int> UndoAllAsync(string type)
    {
        var items = _suggestions.Where(s => s.Type == type && CanUndo(s) && (s.AutoApplied || s.Origin == "email"));
        var n = 0;
        foreach (var s in items)
        {
            try { await UndoAsync(s.Id); n++; }
            catch (Exception ex) { _log.LogWarning(ex, "Undo failed for {Id}", s.Id); }
        }
        return n;
    }

    /// <summary>Reverses an applied task / shopping / pantry suggestion.</summary>
    public async Task UndoAsync(Guid id)
    {
        var s = _suggestions.Get(id);
        if (s is null || !CanUndo(s)) return;
        if (!s.Extra.ContainsKey("undo") && s.Type == "renewal")
        {
            // Created before undo tracking: find it by name.
            var name = s.Result["New renewal tracked:".Length..].Trim();
            var repo = _sp.GetRequiredService<RenewalService>().Repo;
            var match = repo.Where(r => r.Name == name).OrderBy(r => Math.Abs((r.CreatedUtc - s.UpdatedUtc).TotalSeconds)).FirstOrDefault();
            if (match is not null) await repo.DeleteAsync(match.Id);
            s.Status = SuggestionStatus.Undone;
            s.Result = "Undone — " + s.Result;
            await _suggestions.UpsertAsync(s);
            return;
        }
        var parts = s.Extra["undo"].Split(':');
        var target = Guid.Parse(parts[1]);
        switch (parts[0])
        {
            case "task":
                await _sp.GetRequiredService<TaskService>().DeleteAsync(target);
                break;
            case "shopping":
                await _sp.GetRequiredService<KitchenService>().Shopping.DeleteAsync(target);
                break;
            case "shopping-qty":
                var kitchen = _sp.GetRequiredService<KitchenService>();
                if (kitchen.Shopping.Get(target) is { } item)
                {
                    item.Quantity -= decimal.Parse(parts[2], CultureInfo.InvariantCulture);
                    if (item.Quantity <= 0) await kitchen.Shopping.DeleteAsync(item.Id);
                    else await kitchen.Shopping.UpsertAsync(item);
                }
                break;
            case "promotion":
                await _sp.GetRequiredService<IRepository<Promotion>>().DeleteAsync(target);
                break;
            case "renewal":
                await _sp.GetRequiredService<RenewalService>().Repo.DeleteAsync(target);
                break;
            case "taskstack":
                var ts = _sp.GetRequiredService<TaskService>();
                if (ts.Repo.Get(target) is { } stacked)
                {
                    stacked.Occurrences = int.TryParse(parts[2], out var prev) ? Math.Max(1, prev) : Math.Max(1, stacked.Occurrences - 1);
                    if (parts.Length > 3 && Guid.TryParse(parts[3], out var em)) stacked.LinkedEmailIds.Remove(em);
                    await ts.Repo.UpsertAsync(stacked);
                }
                break;
            case "pantry":
                var k = _sp.GetRequiredService<KitchenService>();
                if (k.Pantry.Get(target) is { } p) await k.UndoAddAsync(p);
                break;
        }
        s.Status = SuggestionStatus.Undone;
        s.Result = "Undone — " + s.Result;
        await _suggestions.UpsertAsync(s);
    }

    public async Task<AiSuggestion?> ApplyAsync(Guid id, bool auto = false, CancellationToken ct = default)
    {
        var s = _suggestions.Get(id);
        if (s is null || s.Status == SuggestionStatus.Applied) return s;
        var handler = _handlers.FirstOrDefault(h => h.Type == s.Type);
        try
        {
            if (handler is null) throw new InvalidOperationException($"No handler for '{s.Type}'");
            s.Result = await handler.ApplyAsync(s, ct);
            s.Status = SuggestionStatus.Applied;
            s.AutoApplied = auto;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Applying suggestion {Id} failed", id);
            s.Status = SuggestionStatus.Failed;
            s.Result = ex.Message;
        }
        await _suggestions.UpsertAsync(s);
        return s;
    }

    public async Task RejectAsync(Guid id)
    {
        var s = _suggestions.Get(id);
        if (s is null) return;
        s.Status = SuggestionStatus.Rejected;
        await _suggestions.UpsertAsync(s);
    }

    public Task PruneAsync(int days = 60)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return _suggestions.DeleteManyAsync(_suggestions.Where(s => s.Status != SuggestionStatus.Pending && s.UpdatedUtc < cutoff).Select(s => s.Id));
    }

    private static readonly System.Text.RegularExpressions.Regex Boilerplate = new(
        @"unsubscribe|opt[- ]?out|privacy|terms( and| &| of)?|policy|preferences|contact (us|information|details)|legal|cookie (policy|settings)|" +
        @"view (this )?(email )?in (your )?browser|returns|order details|collection information|new arrivals|featured|categories|" +
        @"if you are|no (direct|specific|clear)|not (applicable|specified|provided)|^n/?a$|^none$|^other$|^browse|^explore|^discover|^shop |^manage ",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Titles that are clearly not a real item: too long or a sentence (any type); email boilerplate such as
    /// "Returns policy" or "Browse new arrivals" (only for list-type actions, so real tasks like
    /// "Renew insurance policy" are kept).
    /// </summary>
    public static bool IsJunkTitle(string title, string type)
    {
        var t = title.Trim();
        if (t.Length < 2 || t.Length > 80) return true;
        if (t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 12) return true;
        if (t.EndsWith('.') && t.Split(' ').Length > 6) return true;
        return type is "shopping" or "pantry" or "renewal" && Boilerplate.IsMatch(t);
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString() : null;

    private static double? Num(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }
}
