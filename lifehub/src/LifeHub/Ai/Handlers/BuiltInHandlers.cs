namespace LifeHub.Ai.Handlers;

public sealed class TaskActionHandler : IAiActionHandler
{
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    public TaskActionHandler(TaskService tasks, SettingsService settings) { _tasks = tasks; _settings = settings; }

    public string Type => "task";
    public string Label => "Task";
    public string Instructions =>
        "Something the user must DO (reply, pay, book, fix, send, sign, prepare). title = imperative, e.g. 'Pay council tax'. " +
        "date = deadline if any, time = deadline time if stated. duration_minutes = rough effort estimate. " +
        "category must be one of the task categories. priority low|normal|high|urgent.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var cats = _settings.Current.General.TaskCategories;
        var category = cats.FirstOrDefault(c => c.Equals(s.Category, StringComparison.OrdinalIgnoreCase)) ?? cats.FirstOrDefault() ?? "Admin";
        var priority = Enum.TryParse<TaskPriority>(s.Priority, true, out var p) ? p : TaskPriority.Normal;
        // Same issue already open (e.g. repeated alerts)? Stack it instead of creating a duplicate.
        var existing = _tasks.FindOpenDuplicate(s.Title);
        if (existing is not null)
        {
            var before = existing.Occurrences;
            var note = string.IsNullOrWhiteSpace(s.OriginText) ? s.Description : s.OriginText;
            await _tasks.StackAsync(existing, note, s.EmailId, s.Date, priority);
            s.Extra["undo"] = $"taskstack:{existing.Id}:{before}:{s.EmailId}";
            return $"Added to existing task \"{existing.Title}\" (now ×{existing.Occurrences})";
        }
        var task = new TaskItem
        {
            Title = s.Title,
            Notes = s.Description,
            Category = category,
            Priority = priority,
            DueDate = s.Date,
            DueTime = s.Time,
            EstimatedMinutes = s.DurationMinutes,
            Source = s.Origin,
            SourceRef = s.EmailId?.ToString() ?? ""
        };
        await _tasks.SaveAsync(task);
        s.Extra["undo"] = $"task:{task.Id}";
        return $"Task added{(task.DueDate is { } d ? $" (due {d:ddd d MMM})" : "")}";
    }
}

public sealed class EventActionHandler : IAiActionHandler
{
    private readonly CalendarService _calendar;
    private readonly TaskService _tasks;
    private readonly SettingsService _settings;
    public EventActionHandler(CalendarService calendar, TaskService tasks, SettingsService settings) { _calendar = calendar; _tasks = tasks; _settings = settings; }

    public string Type => "event";
    public string Label => "Calendar event";
    public string Instructions =>
        "Something happening at a specific date (and usually time): meeting, appointment, delivery slot, booking, flight, party. " +
        "date required. time = start time (omit for all-day). duration_minutes = length (default 60). description = location/joining details.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        if (s.Date is null) throw new InvalidOperationException("Event has no date");
        var allDay = s.Time is null;
        var start = s.Date.Value.ToDateTime(s.Time ?? TimeOnly.MinValue);
        var end = allDay ? start.AddDays(1) : start.AddMinutes(s.DurationMinutes is > 0 ? s.DurationMinutes.Value : 60);

        if (!_calendar.CanCreateEvents)
        {
            // No writable calendar: fall back to a scheduled task so nothing is lost.
            await _tasks.SaveAsync(new TaskItem
            {
                Title = s.Title, Notes = s.Description, DueDate = s.Date, DueTime = s.Time,
                ScheduledStart = allDay ? null : start, EstimatedMinutes = s.DurationMinutes,
                Category = _settings.Current.General.TaskCategories.FirstOrDefault() ?? "Admin", Source = s.Origin
            });
            return "No writable calendar configured — added as a scheduled task instead";
        }
        var where = await _calendar.CreateEventAsync(s.Title, start, end, s.Description, allDay);
        return $"Added to {where}";
    }
}

public sealed class RenewalActionHandler : IAiActionHandler
{
    private readonly RenewalService _renewals;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    public RenewalActionHandler(RenewalService renewals, SettingsService settings, Clock clock) { _renewals = renewals; _settings = settings; _clock = clock; }

    public string Type => "renewal";
    public string Label => "Renewal / subscription";
    public string Instructions =>
        "A subscription, domain, hosting, insurance or service renewal notice, invoice, or PRICE CHANGE. title = the service name only " +
        "(e.g. 'Netflix', 'example.com'). date = next renewal/payment date. amount = new price per billing period, currency = ISO code. " +
        "description = billing cycle (monthly/yearly) and anything notable such as a price increase.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var existing = _renewals.Repo.All().FirstOrDefault(r =>
            r.Name.Equals(s.Title, StringComparison.OrdinalIgnoreCase) ||
            r.Name.Contains(s.Title, StringComparison.OrdinalIgnoreCase) ||
            s.Title.Contains(r.Name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var notes = new List<string>();
            if (s.Amount is { } amt && amt > 0 && amt != existing.Cost)
            {
                notes.Add($"price {existing.Cost:0.00} → {amt:0.00}");
                existing.Cost = amt;
            }
            if (s.Date is { } d && d != existing.NextRenewal && d >= _clock.Today)
            {
                notes.Add($"renews {d:d MMM yyyy}");
                existing.NextRenewal = d;
            }
            await _renewals.SaveAsync(existing, "From email: " + s.Description);
            return notes.Count == 0 ? $"Matched {existing.Name}, nothing changed" : $"Updated {existing.Name}: {string.Join(", ", notes)}";
        }

        // Only track genuinely new subscriptions: a short service name and a real price.
        if (s.Amount is not > 0) throw new InvalidOperationException("No price found — not added as a renewal");
        if (s.Title.Length > 40 || s.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5)
            throw new InvalidOperationException("Doesn't look like a service name — not added as a renewal");
        var desc = s.Description.ToLowerInvariant();
        var cycle = desc.Contains("year") || desc.Contains("annual") ? BillingCycle.Yearly
                  : desc.Contains("quarter") ? BillingCycle.Quarterly
                  : desc.Contains("week") ? BillingCycle.Weekly : BillingCycle.Monthly;
        var r = new Renewal
        {
            Name = s.Title, Cost = s.Amount ?? 0,
            Currency = s.Currency is { Length: 3 } c && c.All(char.IsLetter) ? c.ToUpperInvariant() : _settings.Current.General.Currency,
            NextRenewal = s.Date ?? _clock.Today.AddMonths(1), Cycle = cycle, Notes = s.Description,
            Kind = s.Title.Contains('.') && !s.Title.Contains(' ') ? RenewalKind.Domain : RenewalKind.Subscription
        };
        await _renewals.SaveAsync(r);
        s.Extra["undo"] = $"renewal:{r.Id}";
        return $"New renewal tracked: {r.Name}";
    }
}

public sealed class ShoppingActionHandler : IAiActionHandler
{
    private readonly KitchenService _kitchen;
    public ShoppingActionHandler(KitchenService kitchen) => _kitchen = kitchen;

    public string Type => "shopping";
    public string Label => "Shopping list";
    public string Instructions =>
        "Something the user or their household NEEDS or ASKED to buy (e.g. 'we're out of milk', 'can you pick up nappies'). " +
        "One action per item. title = item name (e.g. 'Milk'). amount = quantity (default 1). description = unit or notes. " +
        "Never for products that are merely advertised or on offer — those are promotions.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var qty = s.Amount is > 0 ? s.Amount.Value : 1;
        var existed = _kitchen.Shopping.Where(x => !x.Checked && x.Name.Equals(s.Title.Trim(), StringComparison.OrdinalIgnoreCase)).Count > 0;
        var item = await _kitchen.AddToShoppingAsync(s.Title, qty, "", s.Origin == "email" ? "AI" : "Capture");
        s.Extra["undo"] = existed ? $"shopping-qty:{item.Id}:{qty.ToString(System.Globalization.CultureInfo.InvariantCulture)}" : $"shopping:{item.Id}";
        return "Added to shopping list";
    }
}

public sealed class PantryActionHandler : IAiActionHandler
{
    private readonly KitchenService _kitchen;
    private readonly SettingsService _settings;
    public PantryActionHandler(KitchenService kitchen, SettingsService settings) { _kitchen = kitchen; _settings = settings; }

    public string Type => "pantry";
    public string Label => "Add to kitchen stock";
    public string Instructions =>
        "Groceries or household supplies that HAVE ARRIVED: supermarket delivery confirmations/receipts (Tesco, Sainsbury's, Asda, Ocado, " +
        "Morrisons, Aldi, Amazon Fresh…) or the user saying 'I bought…'. NEVER for takeaway/restaurant orders (McDonald's, Deliveroo, " +
        "Uber Eats, Just Eat), newsletters, devotionals, articles or adverts. One action per item. " +
        "title = item, amount = quantity, date = use-by date if known, category = one of the food categories, description = 'freezer' if it went in the freezer.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var cats = _settings.Current.General.FoodCategories;
        var category = cats.FirstOrDefault(c => c.Equals(s.Category, StringComparison.OrdinalIgnoreCase)) ?? "Other";
        var location = s.Description.Contains("freez", StringComparison.OrdinalIgnoreCase) ? StorageLocation.Freezer : KitchenService.GuessLocation(category);
        var item = await _kitchen.AddToPantryAsync(new PantryItem
        {
            Name = s.Title, Quantity = s.Amount is > 0 ? s.Amount.Value : 1, Category = category, Location = location, UseBy = s.Date
        });
        s.Extra["undo"] = $"pantry:{item.Id}";
        // Bought it, so tick it off the shopping list if it was there.
        var onList = _kitchen.Shopping.Where(x => !x.Checked && x.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        await _kitchen.Shopping.DeleteManyAsync(onList.Select(x => x.Id));
        return $"Added to {item.Location.ToString().ToLower()}";
    }
}

public sealed class PromotionActionHandler : IAiActionHandler
{
    private readonly IRepository<Promotion> _promotions;
    private readonly IRepository<EmailRecord> _emails;
    private readonly SettingsService _settings;

    public PromotionActionHandler(IRepository<Promotion> promotions, IRepository<EmailRecord> emails, SettingsService settings)
    {
        _promotions = promotions; _emails = emails; _settings = settings;
    }

    public string Type => "promotion";
    public string Label => "Promotion / deal";
    public string Instructions =>
        "ONLY for promotion/newsletter emails (adverts, sales, offers, new games/products, holiday deals). At most one per email. " +
        "title = what's on offer in a few words (e.g. '30% off flights to Spain', 'New game: Space Raiders pre-order'). " +
        "description = the brand/shop and the key details. amount = headline price if stated, currency = ISO code, " +
        "date = when the offer ends if stated (else \"\").";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        if (!_settings.Current.Email.KeepPromotions) return "Ignored (promotions list is switched off)";
        var email = s.EmailId is { } id ? _emails.Get(id) : null;
        var merchant = email?.From ?? "";
        var lt = merchant.IndexOf('<');
        if (lt > 0) merchant = merchant[..lt].Trim().Trim('"');
        var p = new Promotion
        {
            Title = s.Title, Description = s.Description, Merchant = merchant, Price = s.Amount,
            Currency = s.Currency ?? "", EndsOn = s.Date, EmailId = s.EmailId, WebLink = email?.WebLink ?? ""
        };
        await _promotions.UpsertAsync(p);
        s.Extra["undo"] = $"promotion:{p.Id}";
        return "Saved to Promotions";
    }
}
