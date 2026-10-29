using System.Globalization;
using LifeHub.Ai;
using LifeHub.Integrations.HomeAssistant;

namespace LifeHub.Services;

/// <summary>
/// The once-a-minute heartbeat: reminders, daily digest, publishing LifeHub sensors to Home
/// Assistant, presence rules, auto shopping list, renewal roll-forward and nightly backups.
/// </summary>
public sealed class AutomationService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<AutomationService> _log;
    private readonly Dictionary<string, string> _lastPresence = new();
    private DateTime _lastSensorsUtc = DateTime.MinValue;
    private DateTime _lastHourlyUtc = DateTime.MinValue;

    private readonly Dictionary<Guid, DateOnly> _eveningPlannedFor = new(), _morningPlannedFor = new();

    public DateTime? LastSensorPublishUtc { get; private set; }
    public string LastAutoPlan { get; private set; } = "";
    public string LastSensorError { get; private set; } = "";

    public AutomationService(IServiceProvider sp, ILogger<AutomationService> log)
    {
        _sp = sp; _log = log;
    }

    private T S<T>() where T : notnull => _sp.GetRequiredService<T>();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var system = UserScope.AsSystem();
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            await Step("hourly", HourlyAsync);
            await EachPerson("reminders", RemindersAsync);
            await Step("presence", PresenceAsync);
            await EachPerson("autoplan", AutoPlanAsync);
            await Step("sensors", () => PublishSensorsAsync(force: false));
        } while (await timer.WaitForNextTickAsync(ct));
    }

    /// <summary>Runs a step once for each person, as that person (their tasks, their phones, their schedule).</summary>
    private async Task EachPerson(string name, Func<Task> f)
    {
        foreach (var user in S<SettingsService>().Users)
            using (UserScope.As(user.Id))
                await Step($"{name} ({user.FirstName})", f);
    }

    private async Task Step(string name, Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { _log.LogWarning(ex, "Automation step {Step} failed", name); }
    }

    private async Task HourlyAsync()
    {
        if (DateTime.UtcNow - _lastHourlyUtc < TimeSpan.FromHours(1)) return;
        _lastHourlyUtc = DateTime.UtcNow;
        await S<RenewalService>().RollForwardAsync();
        // First connection to Home Assistant: pick sensible defaults automatically.
        var haSettings = S<SettingsService>().Current.HomeAssistant;
        if (S<HomeAssistantClient>().IsConfigured && !haSettings.AutoSetupDone)
        {
            // Phones and calendars found go to the main person; others pick theirs in Settings → Home Assistant.
            try { using (UserScope.As(S<SettingsService>().PrimaryUserId)) await S<HomeAssistantSetup>().AutoConfigureAsync(redo: false); }
            catch (Exception ex) { _log.LogInformation("HA auto-setup postponed: {Message}", ex.Message); }
        }
        await CleanUpAutoShoppingOnceAsync();
        try { await S<DealsService>().RunIfDueAsync(); }
        catch (Exception ex) { _log.LogInformation("Deals check failed: {Message}", ex.Message); }
        await S<NotificationService>().PruneAsync();
        await S<AiService>().PruneAsync();
        await S<TaskService>().PurgeCompletedAsync(180);
        // Expired / dismissed / month-old promotions are cleared unless you saved them.
        var promos = S<IRepository<Promotion>>();
        var today = S<Clock>().Today;
        await promos.DeleteManyAsync(promos.Where(p => !p.Saved &&
            (p.Dismissed || (p.EndsOn is { } e && e < today) || p.CreatedUtc < DateTime.UtcNow.AddDays(-30))).Select(p => p.Id));
        var backup = S<BackupService>();
        var clock = S<Clock>();
        if (clock.Now.Hour >= 3 && !backup.HasBackupToday()) backup.Create();
    }

    /// <summary>
    /// 1.3: the shopping list only contains what you add (typed, scanned, the linked HA list, or a suggestion you
    /// tapped). Items LifeHub used to add by itself are removed once, and pending email shopping ideas are dismissed.
    /// </summary>
    private async Task CleanUpAutoShoppingOnceAsync()
    {
        var settings = S<SettingsService>();
        if (settings.Current.General.ShoppingSourcesMigrated) return;
        var kitchen = S<KitchenService>();
        var auto = kitchen.Shopping.Where(x => !x.Checked && x.Reason is "Low stock" or "Expired" or "Predicted" or "AI");
        if (auto.Count > 0)
        {
            await kitchen.Shopping.DeleteManyAsync(auto.Select(x => x.Id));
            await S<NotificationService>().NotifyAsync($"🛒 Shopping list tidied: {auto.Count} removed",
                "Items LifeHub had added by itself (low stock, predictions, emails) were removed. From now on only you add items — " +
                "staples that are running low appear as suggestions on the Shopping page.",
                NoticeLevel.Info, "shopping", "shopping-cleanup-1.3", push: false);
        }
        var ai = S<AiService>();
        foreach (var s in ai.Suggestions.Where(x => x.Status == SuggestionStatus.Pending && x.Type == "shopping" && x.Origin == "email"))
            await ai.RejectAsync(s.Id);
        await settings.UpdateAsync(s => s.General.ShoppingSourcesMigrated = true);
    }

    private async Task RemindersAsync()
    {
        var settings = S<SettingsService>().Current;
        var n = settings.Notifications;
        var clock = S<Clock>();
        var notify = S<NotificationService>();
        var today = clock.Today;
        var now = clock.Now;
        var culture = CultureInfo.GetCultureInfo(settings.General.Culture);

        if (n.RenewalReminders)
        {
            foreach (var r in S<RenewalService>().Active())
            {
                var days = r.DaysUntil(today);
                var stage = days <= 0 ? "today" : days == 1 ? "tomorrow" : days <= r.RemindDaysBefore ? "window" : null;
                if (stage is null || days < -1) continue;
                var when = days <= 0 ? "today" : days == 1 ? "tomorrow" : $"in {days} days ({r.NextRenewal.ToString("d MMM", culture)})";
                var verb = r.Status == RenewalStatus.Trial ? "trial ends" : r.AutoRenew ? "renews" : "expires";
                await notify.NotifyAsync($"🔁 {r.Name} {verb} {when}",
                    $"{Components.Shared.Fmt.Money(r.Cost, r.Currency)} {r.CycleLabel}{(r.AutoRenew ? "" : "\nManual renewal needed")}",
                    days <= 1 ? NoticeLevel.Alert : NoticeLevel.Warning, "renewals", $"renewal:{r.Id}:{r.NextRenewal}:{stage}", tag: $"renewal-{r.Id}");
            }
        }

        if (n.TaskReminders)
        {
            foreach (var t in S<TaskService>().Open())
            {
                // 15 minutes before a timed deadline or a scheduled slot.
                var at = t.ScheduledStart ?? (t.DueTime is not null ? t.DueAt : null);
                if (at is { } a && a > now && a - now <= TimeSpan.FromMinutes(15))
                    await notify.NotifyAsync($"⏰ {t.Title}", $"{(t.ScheduledStart is not null ? "Planned" : "Due")} at {a:HH:mm} · {t.Category}",
                        NoticeLevel.Info, "tasks", $"task:{t.Id}:{a:yyyyMMddHHmm}", tag: $"task-{t.Id}");
            }
        }

        if (n.ExpiryReminders)
        {
            var warn = settings.General.ExpiryWarningDays;
            var kitchen = S<KitchenService>();
            var expiring = kitchen.NeedsAttention(warn).Where(p => p.Location != StorageLocation.Freezer).ToList();
            if (expiring.Count > 0)
            {
                var list = NotificationService.Bullets(expiring.Select(p => $"{p.Name} — {kitchen.Fresh(p).Label}"), 6);
                // Once a day, and again if the set of items changes.
                var key = $"food:{today}:{string.Join(",", expiring.Select(e => e.Id.ToString()[..8]).OrderBy(x => x))}";
                if (now.Hour >= 9)
                    await notify.NotifyAsync($"🥛 Use up soon ({expiring.Count})", list, NoticeLevel.Warning, "kitchen", key, tag: "food");
            }
        }

        if (n.DailyDigest && TimeOnly.FromDateTime(now) >= n.DailyDigestTime)
        {
            var key = $"digest:{today}";
            if (notify.Repo.Where(x => x.DedupeKey == key).Count == 0)
            {
                var tasks = S<TaskService>().DueToday();
                var overdue = tasks.Count(t => t.IsOverdue(now));
                var renewals = S<RenewalService>().DueWithin(7);
                var food = S<KitchenService>().ExpiringWithin(settings.General.ExpiryWarningDays);
                var shopping = S<KitchenService>().Shopping.Where(s => !s.Checked).Count;
                var events = await S<CalendarService>().GetEventsAsync(today, today);
                var sections = new List<string>();
                if (events.Count > 0)
                    sections.Add($"📅 Today\n" + NotificationService.Bullets(events.Select(e => e.AllDay ? $"All day · {e.Title}" : $"{e.Start:HH:mm} · {e.Title}"), 4));
                if (tasks.Count > 0)
                    sections.Add($"✅ {tasks.Count} task{(tasks.Count == 1 ? "" : "s")} due{(overdue > 0 ? $" ({overdue} overdue)" : "")}\n" +
                                 NotificationService.Bullets(tasks.Select(t => t.Occurrences > 1 ? $"{t.Title} (×{t.Occurrences})" : t.Title), 4));
                if (renewals.Count > 0)
                    sections.Add("🔁 Renewing this week\n" + NotificationService.Bullets(renewals.Select(r => $"{r.Name} · {r.NextRenewal.ToString("ddd", culture)} · {Components.Shared.Fmt.Money(r.Cost, r.Currency)}"), 3));
                var meals = S<MealService>().EntriesFor(today, today).Where(e => !e.Cooked).ToList();
                if (meals.Count > 0)
                    sections.Add("🍽 Meals today\n" + NotificationService.Bullets(meals.Select(e => $"{e.Slot} · {S<MealService>().TitleOf(e)}"), 3));
                if (settings.Bible.Enabled && S<BibleService>().VerseFor(today) is { Reference.Length: > 0 } verse)
                    sections.Add($"📖 Verse of the day · {verse.Reference}");
                var extras = new List<string>();
                if (food.Count > 0) extras.Add($"🥛 {food.Count} to use up");
                if (shopping > 0) extras.Add($"🛒 {shopping} to buy");
                if (extras.Count > 0) sections.Add(string.Join("   ", extras));
                await notify.NotifyAsync($"☀️ Good morning — {now.ToString("dddd d MMM", culture)}",
                    sections.Count == 0 ? "Nothing urgent today. Enjoy it!" : string.Join("\n\n", sections), NoticeLevel.Info, "", key, tag: "digest");
            }
        }
    }

    /// <summary>
    /// Evening: plans tomorrow. Morning: fits anything new into the rest of today (e.g. tasks the
    /// AI created from overnight email). Only unplanned tasks are touched, so re-running is safe.
    /// </summary>
    private async Task AutoPlanAsync()
    {
        var s = S<SettingsService>().Current.Schedule;
        var now = S<Clock>().Now;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var who = UserScope.UserId ?? Guid.Empty;

        if (s.AutoPlanEvening && time >= s.AutoPlanEveningTime && _eveningPlannedFor.GetValueOrDefault(who) != today.AddDays(1))
        {
            _eveningPlannedFor[who] = today.AddDays(1);
            await RunPlanAsync(today.AddDays(1), "Tomorrow", s);
        }
        if (s.AutoPlanMorning && time >= s.AutoPlanMorningTime && _morningPlannedFor.GetValueOrDefault(who) != today &&
            (!s.AutoPlanEvening || time < s.AutoPlanEveningTime))
        {
            _morningPlannedFor[who] = today;
            await RunPlanAsync(today, "Today", s);
        }
    }

    /// <summary>Plans one day now (also used by the "Run now" button). Returns a summary line.</summary>
    public async Task<string> RunPlanAsync(DateOnly date, string label, ScheduleSettings s)
    {
        var tasks = S<TaskService>();
        var already = tasks.Open().Count(t => t.ScheduledStart is { } st && DateOnly.FromDateTime(st) == date);
        var room = Math.Max(0, s.AutoPlanMaxTasks - already);
        if (room == 0) return LastAutoPlan = $"{label}: already has {already} planned task(s)";

        var planner = S<PlannerService>();
        var plan = await planner.ProposeAsync(date, maxTasks: room);
        if (plan.Planned.Count == 0 && plan.Unplanned.Count == 0) return LastAutoPlan = $"{label}: nothing new to plan";

        var applied = plan.Planned.Count == 0 ? 0 : await planner.ApplyAsync(plan, s.AutoPlanAddToCalendar);
        var msg = NotificationService.Bullets(plan.Planned.Select(p => $"{p.Start:HH:mm} · {p.Title}"), 6);
        if (plan.Unplanned.Count > 0)
            msg += (msg.Length > 0 ? "\n\n" : "") + "Didn't fit:\n" + NotificationService.Bullets(plan.Unplanned.Select(t => t.Title), 3);
        var title = applied > 0 ? $"🗓 {label} planned: {applied} task{(applied == 1 ? "" : "s")}" : $"🗓 {label}: some tasks didn't fit";
        await S<NotificationService>().NotifyAsync(title, msg, plan.Unplanned.Count > 0 ? NoticeLevel.Warning : NoticeLevel.Info,
            "schedule", $"autoplan:{date}:{label}:{applied}:{plan.Unplanned.Count}", tag: "plan-" + label.ToLowerInvariant());
        return LastAutoPlan = $"{title}. {msg.Replace("\n", " ")}";
    }

    private static string Describe(int? days) => days switch
    {
        null => "no date",
        < 0 => "expired",
        0 => "today",
        1 => "tomorrow",
        _ => $"{days} days"
    };

    private async Task PresenceAsync()
    {
        var ha = S<HomeAssistantClient>();
        var s = S<SettingsService>().Current.HomeAssistant;
        if (!ha.IsConfigured || s.PresenceEntities.Count == 0) return;
        var people = S<SettingsService>().Users;
        foreach (var id in s.PresenceEntities.Union(people.Select(u => u.PersonEntity).Where(p => p.Length > 0)))
        {
            // The shopping reminder goes to the LifeHub person linked to this HA person (else the main person).
            var person = people.FirstOrDefault(u => u.PersonEntity == id) ?? people.FirstOrDefault(u => u.Id == S<SettingsService>().PrimaryUserId);
            using var scope = person is null ? UserScope.AsSystem() : UserScope.As(person.Id);
            var st = await ha.GetStateAsync(id);
            if (st is null) continue;
            if (_lastPresence.TryGetValue(id, out var prev) && prev == "home" && st.State != "home" && s.NotifyShoppingWhenLeaving)
            {
                var items = S<KitchenService>().Shopping.Where(x => !x.Checked);
                if (items.Count > 0)
                {
                    // Only to the phone(s) of the person who left, when we can tell which those are.
                    List<string>? phones = null;
                    try { phones = await ha.PhonesOfAsync(id); } catch { }
                    var lines = items.OrderBy(i => i.Category).ThenBy(i => i.Name)
                        .Select(i => i.Quantity == 1 && string.IsNullOrEmpty(i.Unit) ? i.Name : $"{i.Name} ×{Components.Shared.Fmt.Qty(i.Quantity, i.Unit)}");
                    await S<NotificationService>().NotifyAsync(
                        $"🛒 {NotificationService.FirstName(st.Name)}, while you're out — {items.Count} to buy",
                        NotificationService.Bullets(lines, 8), NoticeLevel.Info, "shopping",
                        $"leave:{id}:{DateTime.UtcNow:yyyyMMddHHmm}", tag: "shopping", onlyTo: phones, person: id);
                }
            }
            _lastPresence[id] = st.State;
        }
    }

    /// <summary>Publishes sensor.lifehub_* entities into HA (every 5 minutes, or immediately when forced).</summary>
    public async Task PublishSensorsAsync(bool force)
    {
        var ha = S<HomeAssistantClient>();
        var settingsService = S<SettingsService>();
        var settings = settingsService.Household;
        if (!ha.IsConfigured || !settings.HomeAssistant.PublishSensors) return;
        if (!force && DateTime.UtcNow - _lastSensorsUtc < TimeSpan.FromMinutes(5)) return;
        _lastSensorsUtc = DateTime.UtcNow;

        // The household sensors show the main person's tasks and schedule; everyone else gets their own tasks sensor.
        using var asMain = UserScope.As(settingsService.PrimaryUserId);
        try
        {
            foreach (var other in settingsService.Users.Where(u => u.Id != settingsService.PrimaryUserId && u.Slug.Length > 0))
            {
                using (UserScope.As(other.Id))
                {
                    var theirs = S<TaskService>().DueToday();
                    await ha.SetStateAsync($"sensor.lifehub_tasks_due_{other.Slug}", theirs.Count.ToString(), new()
                    {
                        ["friendly_name"] = $"LifeHub tasks due ({other.FirstName})", ["icon"] = "mdi:checkbox-marked-circle-outline", ["unit_of_measurement"] = "tasks",
                        ["overdue"] = S<TaskService>().OverdueCount(), ["tasks"] = theirs.Take(10).Select(t => t.Title).ToArray()
                    });
                }
            }
            var clock = S<Clock>();
            var today = clock.Today;
            var period = await S<ScheduleService>().CurrentAsync();
            var tasks = S<TaskService>().DueToday();
            var renewals = S<RenewalService>().DueWithin(settings.General.RenewalWarningDays);
            var food = S<KitchenService>().ExpiringWithin(settings.General.ExpiryWarningDays);
            var shopping = S<KitchenService>().Shopping.Where(x => !x.Checked);
            var pending = S<AiService>().PendingCount;

            await ha.SetStateAsync("sensor.lifehub_current_period", period.Kind?.ToString() ?? "Unscheduled", new()
            {
                ["friendly_name"] = "LifeHub current period", ["icon"] = "mdi:calendar-clock",
                ["label"] = period.Label, ["until"] = period.Until?.ToString("HH:mm"),
                ["next"] = period.NextLabel, ["next_start"] = period.NextStart?.ToString("HH:mm"),
                ["in_event"] = period.Event is not null, ["event"] = period.Event?.Title
            });
            await ha.SetStateAsync("sensor.lifehub_tasks_due", tasks.Count.ToString(), new()
            {
                ["friendly_name"] = "LifeHub tasks due", ["icon"] = "mdi:checkbox-marked-circle-outline", ["unit_of_measurement"] = "tasks",
                ["overdue"] = S<TaskService>().OverdueCount(), ["tasks"] = tasks.Take(10).Select(t => t.Title).ToArray()
            });
            await ha.SetStateAsync("sensor.lifehub_renewals_due", renewals.Count.ToString(), new()
            {
                ["friendly_name"] = "LifeHub renewals due", ["icon"] = "mdi:autorenew", ["unit_of_measurement"] = "items",
                ["items"] = renewals.Take(10).Select(r => $"{r.Name} ({r.NextRenewal:yyyy-MM-dd})").ToArray(),
                ["monthly_total"] = Math.Round(S<RenewalService>().MonthlyTotal(), 2)
            });
            await ha.SetStateAsync("sensor.lifehub_food_expiring", food.Count.ToString(), new()
            {
                ["friendly_name"] = "LifeHub food expiring", ["icon"] = "mdi:fridge-alert-outline", ["unit_of_measurement"] = "items",
                ["items"] = food.Take(15).Select(f => $"{f.Name} ({f.UseBy:yyyy-MM-dd})").ToArray()
            });
            await ha.SetStateAsync("sensor.lifehub_shopping_list", shopping.Count.ToString(), new()
            {
                ["friendly_name"] = "LifeHub shopping list", ["icon"] = "mdi:cart-outline", ["unit_of_measurement"] = "items",
                ["items"] = shopping.Select(x => x.Name).ToArray()
            });
            await ha.SetStateAsync("sensor.lifehub_ai_pending", pending.ToString(), new()
            {
                ["friendly_name"] = "LifeHub AI suggestions", ["icon"] = "mdi:robot-outline", ["unit_of_measurement"] = "items"
            });
            LastSensorPublishUtc = DateTime.UtcNow;
            LastSensorError = "";
        }
        catch (Exception ex)
        {
            LastSensorError = ex.Message;
            throw;
        }
    }
}
