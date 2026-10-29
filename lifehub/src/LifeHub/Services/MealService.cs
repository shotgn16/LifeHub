using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Services;

/// <summary>How much time and energy a day leaves for cooking (from your calendar and schedule, or your own note).</summary>
public sealed record DayContext(DateOnly Date, string Summary, int FreeEveningMinutes, string Energy, string EnergyAuto, bool DayOff,
    List<string> Events, bool Manual);

/// <summary>One thing you'd need to buy (or already have) for the planned meals.</summary>
public sealed class NeedLine
{
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public List<string> Meals { get; set; } = new();
    /// <summary>need · have · have-some · on-list · basic</summary>
    public string Status { get; set; } = "need";
    public string InStock { get; set; } = "";
    public DateOnly FirstNeeded { get; set; }
    public bool Selected { get; set; } = true;
}

/// <summary>
/// Meal planning up to two weeks ahead: what to cook when (fitting your calendar and energy), what to buy,
/// and AI ideas for using food up, meeting dietary needs and improving recipes. The AI only suggests —
/// nothing changes until you press Apply.
/// </summary>
public sealed class MealService
{
    private readonly IRepository<Recipe> _recipes;
    private readonly IRepository<MealPlanEntry> _entries;
    private readonly IRepository<MealDayNote> _notes;
    private readonly IRepository<MealSuggestion> _suggestions;
    private readonly KitchenService _kitchen;
    private readonly ScheduleService _schedule;
    private readonly CalendarService _calendar;
    private readonly OllamaClient _ollama;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    private readonly ILogger<MealService> _log;
    private readonly IRepository<DealOffer> _deals;

    public const int MaxDays = 14;

    public MealService(IRepository<Recipe> recipes, IRepository<MealPlanEntry> entries, IRepository<MealDayNote> notes,
        IRepository<MealSuggestion> suggestions, KitchenService kitchen, ScheduleService schedule, CalendarService calendar,
        OllamaClient ollama, SettingsService settings, Clock clock, ILogger<MealService> log, IRepository<DealOffer> deals)
    {
        _deals = deals;
        _recipes = recipes; _entries = entries; _notes = notes; _suggestions = suggestions; _kitchen = kitchen;
        _schedule = schedule; _calendar = calendar; _ollama = ollama; _settings = settings; _clock = clock; _log = log;
    }

    public IRepository<Recipe> Recipes => _recipes;
    public IRepository<MealPlanEntry> Entries => _entries;
    public IRepository<MealDayNote> Notes => _notes;
    public IRepository<MealSuggestion> Suggestions => _suggestions;
    public bool AiAvailable => _ollama.IsConfigured;

    public IReadOnlyList<MealPlanEntry> EntriesFor(DateOnly from, DateOnly to) =>
        _entries.Where(e => e.Date >= from && e.Date <= to).OrderBy(e => e.Date).ThenBy(e => SlotOrder(e.Slot)).ToList();

    private int SlotOrder(string slot)
    {
        var i = _settings.Current.Meals.Slots.IndexOf(slot);
        return i < 0 ? 99 : i;
    }

    public string TitleOf(MealPlanEntry e) => e.RecipeId is { } id && _recipes.Get(id) is { } r ? r.Name : e.Title;

    // ------------------------------------------------------------------ day context

    public async Task<List<DayContext>> DayContextsAsync(DateOnly from, DateOnly to)
    {
        List<CalendarEvent> events;
        try { events = await _calendar.GetEventsAsync(from, to); }
        catch (Exception ex) { _log.LogInformation("Calendar unavailable for meal plan: {Message}", ex.Message); events = new(); }
        // Shared meal planning: other people's plans matter too (someone out for dinner, a late meeting).
        var others = new List<CalendarEvent>();
        if (UserScope.Household.Meals && UserScope.UserId is { } me)
            foreach (var user in _settings.Users.Where(u => u.Id != me))
                using (UserScope.As(user.Id))
                {
                    try
                    {
                        foreach (var e in await _calendar.GetEventsAsync(from, to))
                        {
                            if (events.Any(x => x.Start == e.Start && x.Title == e.Title)) continue;   // a family calendar you both see
                            var copy = Json.Clone(e);
                            copy.Title = $"{user.FirstName}: {e.Title}";
                            others.Add(copy);
                        }
                    }
                    catch (Exception ex) { _log.LogDebug(ex, "Calendar of {Name} unavailable", user.FirstName); }
                }
        var list = new List<DayContext>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dayEvents = events.Where(e => DateOnly.FromDateTime(e.Start) <= d && DateOnly.FromDateTime(e.End.AddSeconds(-1)) >= d).ToList();
            var dayOff = _schedule.IsDayOff(d, dayEvents);
            var periods = _schedule.Periods(d, dayEvents);
            // Cooking window: 16:30–20:30, minus work and events.
            var winStart = d.ToDateTime(new TimeOnly(16, 30));
            var winEnd = d.ToDateTime(new TimeOnly(20, 30));
            var busy = periods.Where(p => p.Kind == PeriodKind.Work || p.Kind == PeriodKind.Commute).Select(p => (p.Start, p.End))
                .Concat(dayEvents.Where(e => !e.AllDay).Select(e => (e.Start, e.End)))
                .Select(x => (Start: x.Item1 < winStart ? winStart : x.Item1, End: x.Item2 > winEnd ? winEnd : x.Item2))
                .Where(x => x.End > x.Start).OrderBy(x => x.Start).ToList();
            var busyMinutes = 0.0;
            var cursor = winStart;
            foreach (var (s, e) in busy)
            {
                var start = s < cursor ? cursor : s;
                if (e > start) { busyMinutes += (e - start).TotalMinutes; cursor = e; }
            }
            var free = (int)Math.Max(0, (winEnd - winStart).TotalMinutes - busyMinutes);
            var timed = dayEvents.Where(e => !e.AllDay).ToList();
            var workEnd = periods.Where(p => p.Kind == PeriodKind.Work).Select(p => p.End).DefaultIfEmpty().Max();
            var eveningEvents = timed.Count(e => e.End.TimeOfDay > TimeSpan.FromHours(16.5) && e.Start.TimeOfDay < TimeSpan.FromHours(21));
            var workDay = !dayOff && periods.Any(p => p.Kind == PeriodKind.Work);
            var auto = timed.Count >= 4 || free < 75 || eveningEvents >= 2 ? "low"
                : dayOff || (!workDay && free >= 180 && timed.Count <= 1) ? "high" : "normal";
            var note = _notes.Where(n => n.Date == d).FirstOrDefault();
            var energy = string.IsNullOrEmpty(note?.Energy) ? auto : note!.Energy;
            var parts = new List<string>();
            if (dayOff) parts.Add("Day off");
            else if (workEnd != default) parts.Add($"Work till {workEnd:HH:mm}");
            if (timed.Count > 0) parts.Add($"{timed.Count} event{(timed.Count == 1 ? "" : "s")}");
            parts.Add(free >= 60 ? $"{free / 60}h{(free % 60 > 0 ? $" {free % 60}m" : "")} free evening" : free > 0 ? $"{free}m free evening" : "busy evening");
            var theirs = others.Where(e => DateOnly.FromDateTime(e.Start) <= d && DateOnly.FromDateTime(e.End.AddSeconds(-1)) >= d);
            list.Add(new DayContext(d, string.Join(" · ", parts), free, energy, auto, dayOff,
                dayEvents.Concat(theirs).Select(e => e.AllDay ? e.Title : $"{e.Start:HH:mm} {e.Title}").ToList(), !string.IsNullOrEmpty(note?.Energy)));
        }
        return list;
    }

    public async Task SetEnergyAsync(DateOnly date, string energy)
    {
        var note = _notes.Where(n => n.Date == date).FirstOrDefault() ?? new MealDayNote { Date = date };
        note.Energy = energy;
        await _notes.UpsertAsync(note);
    }

    // ------------------------------------------------------------------ what to buy

    private static readonly HashSet<string> Basics = new(StringComparer.OrdinalIgnoreCase)
        { "water", "salt", "black pepper", "salt and pepper", "pepper to taste", "ice", "boiling water", "cold water" };

    private static string Key(string name)
    {
        var n = Regex.Replace(name.ToLowerInvariant(), @"\(.*?\)|,.*$", "").Trim();
        n = Regex.Replace(n, @"\b(fresh|large|small|medium|chopped|sliced|diced|minced|grated|free[- ]range|organic|ripe|tinned|canned)\b", "");
        n = Regex.Replace(n, @"\s+", " ").Trim();
        return n.EndsWith("es") && n.Length > 4 && !n.EndsWith("ses") ? n[..^1] : n.EndsWith('s') && n.Length > 3 && !n.EndsWith("ss") ? n[..^1] : n;
    }

    public List<NeedLine> Needs(DateOnly from, DateOnly to)
    {
        var today = _clock.Today;
        var lines = new Dictionary<string, NeedLine>();
        foreach (var e in EntriesFor(from, to).Where(e => !e.Cooked && e.Date >= today))
        {
            if (e.RecipeId is not { } rid || _recipes.Get(rid) is not { } recipe) continue;
            var scale = recipe.Servings > 0 ? (decimal)Math.Max(1, e.Servings) / recipe.Servings : 1;
            foreach (var ing in recipe.Ingredients.Where(i => !i.Optional && !string.IsNullOrWhiteSpace(i.Name)))
            {
                var k = Key(ing.Name) + "|" + ing.Unit.Trim().ToLowerInvariant();
                if (!lines.TryGetValue(k, out var line))
                    lines[k] = line = new NeedLine { Name = ing.Name.Trim(), Unit = ing.Unit.Trim(), FirstNeeded = e.Date, Quantity = 0 };
                if (ing.Quantity is { } q && line.Quantity is not null) line.Quantity += Math.Round(q * scale, 2);
                else line.Quantity = null;
                if (!line.Meals.Contains(recipe.Name)) line.Meals.Add(recipe.Name);
                if (e.Date < line.FirstNeeded) line.FirstNeeded = e.Date;
            }
        }
        var pantry = _kitchen.Pantry.All();
        var shopping = _kitchen.Shopping.Where(s => !s.Checked);
        foreach (var line in lines.Values)
        {
            if (line.Quantity == 0) line.Quantity = null;
            if (Basics.Contains(line.Name.Trim())) { line.Status = "basic"; line.Selected = false; continue; }
            var k = Key(line.Name);
            var stock = pantry.Where(p => Key(p.Name).Contains(k) || k.Contains(Key(p.Name)) ||
                                          (_kitchen.StapleOf(p) is { } st && st.Matches(line.Name))).ToList();
            // Ignore stock that will be past it by the time the meal is cooked.
            stock = stock.Where(p => _kitchen.Fresh(p) is var f && (f.CheckBy is null || f.CheckBy >= line.FirstNeeded)).ToList();
            if (shopping.Any(s => Key(s.Name) == k || Key(s.Name).Contains(k) || k.Contains(Key(s.Name))))
            {
                line.Status = "on-list"; line.Selected = false;
            }
            else if (stock.Count > 0)
            {
                var sameUnit = stock.Where(p => string.Equals(p.Unit.Trim(), line.Unit, StringComparison.OrdinalIgnoreCase)).Sum(p => p.Quantity);
                line.InStock = string.Join(", ", stock.Select(p => $"{Components.Shared.Fmt.Qty(p.Quantity, p.Unit)} {p.Name}".Trim()));
                if (line.Quantity is { } need && sameUnit > 0 && sameUnit < need)
                {
                    line.Status = "have-some";
                    line.Quantity = need - sameUnit;
                }
                else { line.Status = "have"; line.Selected = false; }
            }
        }
        return lines.Values.OrderBy(l => l.Status switch { "need" => 0, "have-some" => 1, "on-list" => 2, "have" => 3, _ => 4 })
            .ThenBy(l => l.FirstNeeded).ThenBy(l => l.Name).ToList();
    }

    /// <summary>You chose to add these to the shopping list.</summary>
    public async Task<int> AddToShoppingAsync(IEnumerable<NeedLine> lines)
    {
        var n = 0;
        foreach (var l in lines)
        {
            var countable = string.IsNullOrWhiteSpace(l.Unit) && l.Quantity is { } q && q == Math.Floor(q) && q <= 24;
            await _kitchen.AddToShoppingAsync(l.Name, countable ? l.Quantity!.Value : 1, "", "Meal plan");
            n++;
        }
        return n;
    }

    /// <summary>Food to use up that none of the planned meals uses.</summary>
    public List<PantryItem> UnusedExpiring(DateOnly from, DateOnly to)
    {
        var used = EntriesFor(from, to).Where(e => e.RecipeId is not null).Select(e => _recipes.Get(e.RecipeId!.Value))
            .Where(r => r is not null).SelectMany(r => r!.Ingredients.Select(i => Key(i.Name))).ToHashSet();
        return _kitchen.ExpiringWithin(_settings.Current.General.ExpiryWarningDays + 4)
            .Where(p => p.Location != StorageLocation.Freezer && !used.Any(u => Key(p.Name).Contains(u) || u.Contains(Key(p.Name)))).ToList();
    }

    // ------------------------------------------------------------------ ingredient lines

    private static readonly string[] Units =
    {
        "g", "kg", "mg", "ml", "l", "litre", "litres", "liter", "tsp", "tbsp", "teaspoon", "teaspoons", "tablespoon", "tablespoons",
        "cup", "cups", "oz", "lb", "lbs", "pinch", "pinches", "handful", "handfuls", "clove", "cloves", "tin", "tins", "can", "cans",
        "pack", "packs", "packet", "packets", "jar", "jars", "slice", "slices", "bunch", "bunches", "sprig", "sprigs", "rasher", "rashers",
        "fillet", "fillets", "pint", "pints", "knob", "dash", "splash", "bag", "bags", "head", "heads", "stick", "sticks", "sheet", "sheets"
    };

    /// <summary>"200 g rice", "200g rice", "2 onions, chopped", "1½ tbsp olive oil", "salt (optional)".</summary>
    public static RecipeIngredient ParseIngredient(string line)
    {
        var text = line.Trim().TrimStart('-', '•', '*').Trim();
        var ing = new RecipeIngredient();
        if (Regex.IsMatch(text, @"\(optional\)|\boptional\b", RegexOptions.IgnoreCase))
        {
            ing.Optional = true;
            text = Regex.Replace(text, @"\s*\(optional\)|,?\s*optional", "", RegexOptions.IgnoreCase).Trim();
        }
        text = text.Replace("½", ".5").Replace("¼", ".25").Replace("¾", ".75").Replace("⅓", ".33");
        var m = Regex.Match(text, @"^(\d*\.?\d+)(?:\s*(?:-|to)\s*\d*\.?\d+)?\s*([a-zA-Z]+)?\.?\s+(.*)$");
        if (m.Success && decimal.TryParse(m.Groups[1].Value.StartsWith('.') ? "0" + m.Groups[1].Value : m.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
        {
            ing.Quantity = q;
            var unit = m.Groups[2].Value;
            if (unit.Length > 0 && Units.Contains(unit.ToLowerInvariant())) { ing.Unit = unit.ToLowerInvariant(); text = m.Groups[3].Value; }
            else text = (unit + " " + m.Groups[3].Value).Trim();
        }
        var comma = text.IndexOf(',');
        if (comma > 0) { ing.Notes = text[(comma + 1)..].Trim(); text = text[..comma]; }
        ing.Name = text.Trim();
        return ing;
    }

    public static List<RecipeIngredient> ParseIngredients(string lines) =>
        lines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(l => l.Length > 0).Select(ParseIngredient)
            .Where(i => i.Name.Length > 0).ToList();

    public static string FormatIngredients(IEnumerable<RecipeIngredient> list) => string.Join("\n", list.Select(i => i.ToString()));

    // ------------------------------------------------------------------ diet check (local, instant)

    private static readonly Dictionary<string, string[]> DietWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gluten free"] = new[] { "flour", "pasta", "spaghetti", "penne", "noodle", "bread", "breadcrumb", "couscous", "barley", "rye", "wheat",
            "semolina", "bulgur", "soy sauce", "beer", "tortilla", "pastry", "cracker", "biscuit", "stock cube", "gravy granules", "panko", "naan", "pitta" },
        ["dairy free"] = new[] { "milk", "cheese", "butter", "cream", "yoghurt", "yogurt", "parmesan", "mozzarella", "cheddar", "ghee", "crème fraîche", "creme fraiche" },
        ["vegetarian"] = new[] { "chicken", "beef", "pork", "lamb", "bacon", "ham", "sausage", "mince", "turkey", "fish", "salmon", "tuna", "prawn", "anchov", "chorizo", "gelatine" },
        ["vegan"] = new[] { "chicken", "beef", "pork", "lamb", "bacon", "ham", "sausage", "mince", "turkey", "fish", "salmon", "tuna", "prawn", "anchov",
            "egg", "milk", "cheese", "butter", "cream", "yoghurt", "yogurt", "honey", "gelatine", "mayonnaise" },
        ["nut free"] = new[] { "almond", "walnut", "cashew", "peanut", "hazelnut", "pecan", "pistachio", "nut", "pesto", "praline", "marzipan" },
        ["no pork"] = new[] { "pork", "bacon", "ham", "chorizo", "pancetta", "prosciutto", "salami", "sausage", "lard", "gelatine" },
        ["egg free"] = new[] { "egg", "mayonnaise", "meringue" },
        ["low carb"] = new[] { "pasta", "rice", "bread", "potato", "noodle", "sugar", "flour", "tortilla" },
    };

    /// <summary>The first of your requirements a food item may clash with ("gluten free" for "Garlic bread"), or "".</summary>
    public string DietClash(string item)
    {
        foreach (var req in _settings.Current.Meals.Requirements)
        {
            var key = DietWords.Keys.FirstOrDefault(k => req.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                                         k.Replace(" ", "").Equals(req.Replace(" ", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            var lower = item.ToLowerInvariant();
            if (lower.Contains(key) || lower.Replace("-", " ").Contains(key)) continue;   // "gluten-free bread" is fine
            if (DietWords[key].Any(w => Regex.IsMatch(lower, $@"\b{Regex.Escape(w)}"))) return req;
        }
        return "";
    }

    /// <summary>Ingredients that may clash with your requirements (a quick hint, not a guarantee).</summary>
    public List<string> DietConflicts(Recipe r)
    {
        var hits = new List<string>();
        foreach (var req in _settings.Current.Meals.Requirements)
        {
            var key = DietWords.Keys.FirstOrDefault(k => req.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                                                         k.Replace(" ", "").Equals(req.Replace(" ", "").Replace("-", ""), StringComparison.OrdinalIgnoreCase));
            if (key is null) continue;
            foreach (var i in r.Ingredients)
            {
                var n = i.Name.ToLowerInvariant();
                if (Regex.IsMatch(n, @"gluten[- ]free|\bgf\b|dairy[- ]free|vegan|plant[- ]based|veggie|meat[- ]free") ) continue;
                var w = DietWords[key].FirstOrDefault(word => Regex.IsMatch(n, $@"\b{Regex.Escape(word)}"));
                if (w is not null) hits.Add($"{i.Name} ({key})");
            }
        }
        return hits.Distinct().ToList();
    }

    // ------------------------------------------------------------------ AI

    private static JsonObject RecipeSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["name"] = new JsonObject { ["type"] = "string" },
            ["servings"] = new JsonObject { ["type"] = "integer" },
            ["minutes"] = new JsonObject { ["type"] = "integer" },
            ["effort"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("easy", "medium", "involved") },
            ["ingredients"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["quantity"] = new JsonObject { ["type"] = "number" },
                        ["unit"] = new JsonObject { ["type"] = "string" },
                        ["optional"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("name", "quantity", "unit")
                }
            },
            ["steps"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["change_summary"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("name", "servings", "minutes", "effort", "ingredients", "steps", "change_summary")
    };

    private static Recipe? ParseRecipe(JsonElement j, out string summary)
    {
        summary = j.TryGetProperty("change_summary", out var cs) && cs.ValueKind == JsonValueKind.String ? cs.GetString() ?? "" : "";
        if (j.ValueKind != JsonValueKind.Object || !j.TryGetProperty("ingredients", out var ings) || ings.ValueKind != JsonValueKind.Array) return null;
        string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        var r = new Recipe
        {
            Name = S(j, "name"),
            Servings = j.TryGetProperty("servings", out var sv) && sv.ValueKind == JsonValueKind.Number ? Math.Clamp(sv.GetInt32(), 1, 20) : 2,
            Minutes = j.TryGetProperty("minutes", out var mv) && mv.ValueKind == JsonValueKind.Number ? Math.Clamp(mv.GetInt32(), 1, 600) : null,
            Effort = S(j, "effort") switch { "easy" => MealEffort.Easy, "involved" => MealEffort.Involved, _ => MealEffort.Medium },
            AiGenerated = true
        };
        foreach (var i in ings.EnumerateArray())
        {
            var name = S(i, "name");
            if (name.Length == 0) continue;
            decimal? q = i.TryGetProperty("quantity", out var qv) && qv.ValueKind == JsonValueKind.Number && qv.GetDouble() > 0 ? (decimal)Math.Round(qv.GetDouble(), 2) : null;
            r.Ingredients.Add(new RecipeIngredient { Name = name, Quantity = q, Unit = S(i, "unit"), Optional = i.TryGetProperty("optional", out var ov) && ov.ValueKind == JsonValueKind.True });
        }
        if (j.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            r.Steps = string.Join("\n", steps.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!.Trim()).Where(s => s.Length > 0));
        if (j.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            r.Tags = tags.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().Take(8).ToList();
        return r.Name.Length > 0 && r.Ingredients.Count > 0 ? r : null;
    }

    private string Household()
    {
        var m = _settings.Current.Meals;
        var sb = new StringBuilder();
        sb.AppendLine($"Dietary requirements (must be met): {(m.Requirements.Count == 0 ? "none" : string.Join(", ", m.Requirements))}.");
        if (!string.IsNullOrWhiteSpace(m.Preferences)) sb.AppendLine($"Household preferences: {m.Preferences}");
        sb.AppendLine($"Use UK English and metric units (g, ml, tbsp, tsp) unless the recipe already uses others.");
        return sb.ToString();
    }

    private static string Describe(Recipe r) =>
        $"{r.Name} — serves {r.Servings}, {(r.Minutes is { } m ? $"{m} min, " : "")}{r.Effort.ToString().ToLowerInvariant()}\n" +
        $"Ingredients: {string.Join("; ", r.Ingredients.Select(i => i.ToString()))}\n" +
        (string.IsNullOrWhiteSpace(r.Steps) ? "" : $"Method: {r.Steps.Replace("\n", " / ")}\n");

    public string StockSummary(int max = 40) => StockList(max);

    private string StockList(int max = 40)
    {
        var kitchen = _kitchen.Pantry.All().OrderBy(p => _kitchen.Fresh(p).CheckBy ?? DateOnly.MaxValue).Take(max);
        return string.Join("; ", kitchen.Select(p =>
        {
            var f = _kitchen.Fresh(p);
            return $"{p.Name}{(f.State is FreshState.UseSoon or FreshState.PastBest or FreshState.Expired or FreshState.CheckFirst ? $" (use soon: {f.Label})" : "")}";
        }));
    }

    /// <summary>
    /// An adapted version of a recipe (not saved). Goal examples: "make it gluten free", "use up the spinach and
    /// cream", "make it quicker for a busy night", "improve the flavour".
    /// </summary>
    public async Task<(Recipe Recipe, string Summary)> AdaptAsync(Recipe original, string goal, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var system = $"""
            You are a practical home cook helping a household adapt a recipe. Keep it recognisably the same dish unless
            asked otherwise, keep quantities realistic for the servings, and keep the method clear and short (max 10 steps).
            {Household()}
            change_summary: one or two sentences saying what you changed and why.
            """;
        var user = $"""
            GOAL: {goal}

            RECIPE:
            {Describe(original)}

            In the kitchen now: {StockList()}
            """;
        var json = await _ollama.ChatJsonAsync(system, user, RecipeSchema(), ct);
        var r = ParseRecipe(json, out var summary) ?? throw new InvalidOperationException("The AI didn't return a usable recipe — try again or reword the goal.");
        r.VariantOf = original.Id;
        r.SourceUrl = original.SourceUrl;
        if (string.Equals(r.Name, original.Name, StringComparison.OrdinalIgnoreCase)) r.Name = $"{original.Name} ({ShortGoal(goal)})";
        r.VariantNote = summary;
        return (r, summary);
    }

    private static string ShortGoal(string goal)
    {
        var g = goal.Trim().TrimEnd('.');
        if (Regex.Match(g, @"(gluten|dairy|nut|egg)[- ]free|vegan|vegetarian", RegexOptions.IgnoreCase) is { Success: true } m) return m.Value.ToLowerInvariant();
        return g.Length > 24 ? "adapted" : g.ToLowerInvariant();
    }

    /// <summary>A first draft of a recipe from just a meal name ("chicken fajitas").</summary>
    public async Task<Recipe> DraftAsync(string mealName, int servings, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var system = $"""
            You write clear, realistic home recipes. {Household()}
            Prefer ingredients from the kitchen list where they suit the dish. change_summary: one line describing the recipe.
            """;
        var json = await _ollama.ChatJsonAsync(system, $"Write a recipe for: {mealName}\nServings: {servings}\nIn the kitchen now: {StockList()}", RecipeSchema(), ct);
        var r = ParseRecipe(json, out var summary) ?? throw new InvalidOperationException("The AI didn't return a usable recipe — try again.");
        r.Notes = summary;
        return r;
    }

    /// <summary>
    /// Looks at the whole plan (meals, calendar, energy, food to use up, dietary needs) and suggests moves and
    /// recipe tweaks. Previous pending suggestions for the same range are replaced.
    /// </summary>
    public async Task<List<MealSuggestion>> SuggestAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var days = await DayContextsAsync(from, to);
        var entries = EntriesFor(from, to).Where(e => !e.Cooked).ToList();
        var recipes = entries.Where(e => e.RecipeId is not null).Select(e => _recipes.Get(e.RecipeId!.Value)).Where(r => r is not null).Select(r => r!)
            .DistinctBy(r => r.Id).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("DAYS (energy = how much time/energy that day leaves for cooking):");
        foreach (var d in days)
            sb.AppendLine($"- {d.Date:yyyy-MM-dd} {d.Date.DayOfWeek}: energy {d.Energy}; {d.Summary}{(d.Events.Count > 0 ? "; events: " + string.Join(", ", d.Events.Take(4)) : "")}");
        sb.AppendLine("\nPLANNED MEALS (entry number, date, slot, meal):");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var r = e.RecipeId is { } id ? _recipes.Get(id) : null;
            sb.AppendLine($"E{i + 1}. {e.Date:yyyy-MM-dd} {e.Slot}: {TitleOf(e)}{(r is null ? "" : $" [recipe R{recipes.IndexOf(r) + 1}, {r.Effort.ToString().ToLowerInvariant()}{(r.Minutes is { } m ? $", {m} min" : "")}]")}");
        }
        sb.AppendLine("\nRECIPES:");
        for (var i = 0; i < recipes.Count; i++)
        {
            var conflicts = DietConflicts(recipes[i]);
            sb.AppendLine($"R{i + 1}. {Describe(recipes[i])}{(conflicts.Count > 0 ? $"May not meet requirements: {string.Join(", ", conflicts)}\n" : "")}");
        }
        var unused = UnusedExpiring(from, to);
        sb.AppendLine($"\nFOOD TO USE UP that no planned meal uses: {(unused.Count == 0 ? "none" : string.Join("; ", unused.Select(p => $"{p.Name} ({_kitchen.Fresh(p).Label})")))}");
        sb.AppendLine($"\nEVERYTHING IN THE KITCHEN: {StockList(60)}");
        if (_settings.Current.Deals.Enabled)
        {
            var today = _clock.Today;
            var offers = _deals.Where(o => !o.Dismissed && o.Food && o.Score >= 3 && o.DietWarning.Length == 0 && (o.ValidUntil is null || o.ValidUntil >= today))
                .OrderByDescending(o => o.Score).Take(12).ToList();
            if (offers.Count > 0)
                sb.AppendLine($"\nON OFFER NEARBY (worth using in a tweak if it fits): {string.Join("; ", offers.Select(o => $"{o.Item} {DealsService.PriceText(o)} at {o.Store}"))}");
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["suggestions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("move", "tweak", "use-up", "diet", "tip") },
                            ["title"] = new JsonObject { ["type"] = "string" },
                            ["reason"] = new JsonObject { ["type"] = "string" },
                            ["entry"] = new JsonObject { ["type"] = "integer" },
                            ["new_date"] = new JsonObject { ["type"] = "string" },
                            ["recipe"] = new JsonObject { ["type"] = "integer" },
                            ["adapted_recipe"] = RecipeSchema()
                        },
                        ["required"] = new JsonArray("kind", "title", "reason")
                    }
                }
            },
            ["required"] = new JsonArray("suggestions")
        };
        var system = $"""
            You help a household plan meals for the next {(to.DayNumber - from.DayNumber) + 1} days. Suggest 3–7 practical improvements:
              move    = move meal E<n> to another date (entry = n, new_date = YYYY-MM-DD inside the plan) — e.g. quick/easy
                        meals on low-energy or busy days, involved recipes on free days, and meals that use food going
                        off earlier. If another meal is on that date it will be swapped.
              use-up  = adapt recipe R<n> to use food that needs using up (recipe = n, adapted_recipe = the new version)
              diet    = recipe R<n> doesn't meet a dietary requirement — give the adapted version
              tweak   = a genuine improvement to recipe R<n> (flavour, simplicity, nutrition) with the adapted version
              tip     = anything else useful (batch cooking, leftovers for lunch, freezing portions…), no changes
            Never invent meals that aren't in the plan for move suggestions. Keep the reason to one or two sentences.
            {Household()}
            """;
        var json = await _ollama.ChatJsonAsync(system, sb.ToString(), schema, ct);
        var result = new List<MealSuggestion>();
        if (json.TryGetProperty("suggestions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray())
            {
                string S(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                int I(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                var kind = S("kind");
                var s = new MealSuggestion { Kind = kind, Title = S("title"), Body = S("reason"), PlanFrom = from, PlanTo = to };
                if (s.Title.Length == 0) continue;
                if (kind == "move")
                {
                    var idx = I("entry") - 1;
                    if (idx < 0 || idx >= entries.Count) continue;
                    if (!DateOnly.TryParseExact(S("new_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var nd)) continue;
                    if (nd < from || nd > to || nd < _clock.Today || nd == entries[idx].Date) continue;
                    s.EntryId = entries[idx].Id;
                    s.NewDate = nd;
                }
                else if (kind is "tweak" or "use-up" or "diet")
                {
                    var idx = I("recipe") - 1;
                    if (idx < 0 || idx >= recipes.Count) continue;
                    s.RecipeId = recipes[idx].Id;
                    if (x.TryGetProperty("adapted_recipe", out var ar) && ParseRecipe(ar, out var summary) is { } adapted)
                    {
                        adapted.VariantOf = recipes[idx].Id;
                        adapted.VariantNote = string.IsNullOrWhiteSpace(summary) ? s.Body : summary;
                        if (string.Equals(adapted.Name, recipes[idx].Name, StringComparison.OrdinalIgnoreCase))
                            adapted.Name = $"{recipes[idx].Name} ({(kind == "diet" ? "adapted" : kind == "use-up" ? "use-up" : "improved")})";
                        s.Proposed = adapted;
                    }
                    else continue;
                }
                else s.Kind = "tip";
                result.Add(s);
            }
        }
        await _suggestions.DeleteManyAsync(_suggestions.Where(x => x.Status == "pending").Select(x => x.Id));
        await _suggestions.UpsertManyAsync(result);
        return result;
    }

    /// <summary>Does what a suggestion says (only when you press Apply).</summary>
    public async Task<string> ApplyAsync(Guid suggestionId)
    {
        var s = _suggestions.Get(suggestionId) ?? throw new InvalidOperationException("Suggestion not found");
        string result;
        if (s.Kind == "move" && s.EntryId is { } eid && s.NewDate is { } nd && _entries.Get(eid) is { } entry)
        {
            var other = _entries.Where(e => e.Date == nd && e.Slot == entry.Slot && e.Id != entry.Id).FirstOrDefault();
            var oldDate = entry.Date;
            entry.Date = nd;
            await _entries.UpsertAsync(entry);
            if (other is not null) { other.Date = oldDate; await _entries.UpsertAsync(other); }
            result = other is null ? $"Moved {TitleOf(entry)} to {nd:ddd d MMM}" : $"Swapped {TitleOf(entry)} and {TitleOf(other)}";
        }
        else if (s.Proposed is { } proposed && s.RecipeId is { } rid)
        {
            var variant = await SaveVariantAsync(proposed, rid, s.PlanFrom, s.PlanTo);
            result = $"Saved \"{variant.Name}\" and used it in the plan";
        }
        else result = "Noted";
        s.Status = "applied";
        await _suggestions.UpsertAsync(s);
        return result;
    }

    /// <summary>Saves an adapted recipe as a new one (the original is kept) and uses it for planned meals in the range.</summary>
    public async Task<Recipe> SaveVariantAsync(Recipe proposed, Guid originalId, DateOnly from, DateOnly to)
    {
        var variant = new Recipe
        {
            Name = proposed.Name, Servings = proposed.Servings, Minutes = proposed.Minutes, Effort = proposed.Effort,
            Ingredients = proposed.Ingredients, Steps = proposed.Steps, Tags = proposed.Tags, SourceUrl = proposed.SourceUrl,
            AiGenerated = true, VariantOf = originalId, VariantNote = proposed.VariantNote, Notes = proposed.Notes
        };
        await _recipes.UpsertAsync(variant);
        var affected = _entries.Where(e => e.RecipeId == originalId && e.Date >= from && e.Date <= to && !e.Cooked);
        foreach (var e in affected) e.RecipeId = variant.Id;
        if (affected.Count > 0) await _entries.UpsertManyAsync(affected);
        return variant;
    }

    public async Task DismissAsync(Guid suggestionId)
    {
        if (_suggestions.Get(suggestionId) is { } s) { s.Status = "dismissed"; await _suggestions.UpsertAsync(s); }
    }

    /// <summary>Marks a meal as cooked and (optionally) takes its ingredients out of the kitchen stock.</summary>
    public async Task<int> CookedAsync(Guid entryId, bool useIngredients)
    {
        var e = _entries.Get(entryId);
        if (e is null) return 0;
        e.Cooked = true;
        await _entries.UpsertAsync(e);
        if (!useIngredients || e.RecipeId is not { } rid || _recipes.Get(rid) is not { } r) return 0;
        var used = 0;
        foreach (var ing in r.Ingredients.Where(i => !i.Optional))
        {
            var k = Key(ing.Name);
            var item = _kitchen.Pantry.All()
                .Where(p => Key(p.Name).Contains(k) || k.Contains(Key(p.Name)))
                .OrderBy(p => _kitchen.Fresh(p).CheckBy ?? DateOnly.MaxValue).FirstOrDefault();
            if (item is null) continue;
            // Countable items (3 eggs, 2 onions) come off the stock; packs (flour, rice, milk) are just marked as opened.
            if (string.IsNullOrWhiteSpace(ing.Unit) && ing.Quantity is { } q && string.IsNullOrWhiteSpace(item.Unit))
            {
                await _kitchen.UseAsync(item.Id, q >= item.Quantity ? null : q);
                used++;
            }
            else if (item.OpenedOn is null)
            {
                item.OpenedOn = _clock.Today;
                await _kitchen.SavePantryAsync(item);
            }
        }
        return used;
    }
}
