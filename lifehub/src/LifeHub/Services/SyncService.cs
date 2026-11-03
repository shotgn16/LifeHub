using System.Globalization;
using System.Text.Json;

namespace LifeHub.Services;

/// <summary>
/// Offline support for the phone apps. <see cref="Snapshot"/> is what a phone keeps to show while it can't reach
/// LifeHub; <see cref="ApplyAsync"/> replays what you did offline (added a task, ticked the shopping…) once it's
/// back. Every change carries an id from the phone, so sending the same change twice only applies it once.
/// </summary>
public sealed class SyncService
{
    private readonly TaskService _tasks;
    private readonly KitchenService _kitchen;
    private readonly NotesService _notes;
    private readonly MealService _meals;
    private readonly IRepository<SyncReceipt> _receipts;
    private readonly SettingsService _settings;
    private readonly Clock _clock;
    private readonly ILogger<SyncService> _log;
    private readonly SemaphoreSlim _one = new(1, 1);

    public SyncService(TaskService tasks, KitchenService kitchen, NotesService notes, MealService meals,
        IRepository<SyncReceipt> receipts, SettingsService settings, Clock clock, ILogger<SyncService> log)
    {
        _tasks = tasks; _kitchen = kitchen; _notes = notes; _meals = meals; _receipts = receipts;
        _settings = settings; _clock = clock; _log = log;
    }

    public sealed record Change(string Id, string Type, DateTime? At, JsonElement Data);
    public sealed record Outcome(string Id, string Status, string Message);

    /// <summary>What the phone keeps for offline use (kept small: open items, recent notes, the coming week).</summary>
    public object Snapshot()
    {
        var today = _clock.Today;
        var s = _settings.Current;
        var me = _settings.Me;
        return new
        {
            generatedUtc = DateTime.UtcNow,
            today = today.ToString("yyyy-MM-dd"),
            person = me?.Name ?? "",
            household = s.General.HouseholdName,
            taskCategories = s.General.TaskCategories,
            tasks = _tasks.Ordered(_tasks.Open()).Take(150).Select(t => new
            {
                id = t.Id, t.Title, t.Category, priority = t.Priority.ToString(),
                due = t.DueDate?.ToString("yyyy-MM-dd"), time = t.DueTime?.ToString("HH:mm"), t.Notes
            }),
            shopping = _kitchen.Shopping.All().OrderBy(x => x.Checked).ThenBy(x => x.Category).ThenBy(x => x.Name).Take(200).Select(x => new
            {
                id = x.Id, x.Name, x.Quantity, x.Unit, x.Category, x.Checked
            }),
            notes = _notes.All().Take(100).Select(n => new { id = n.Id, n.Text, n.Category, n.Pinned, created = n.CreatedUtc }),
            meals = _meals.EntriesFor(today, today.AddDays(6)).OrderBy(e => e.Date).Select(e => new
            {
                date = e.Date.ToString("yyyy-MM-dd"), e.Slot, title = _meals.TitleOf(e), e.Cooked
            })
        };
    }

    /// <summary>Applies changes made offline, oldest first. Already-applied ids are skipped.</summary>
    public async Task<List<Outcome>> ApplyAsync(IEnumerable<Change> changes)
    {
        await _one.WaitAsync();
        try
        {
            var done = _receipts.All().ToDictionary(r => r.ClientId, r => r.Result);
            var results = new List<Outcome>();
            foreach (var c in changes.OrderBy(c => c.At ?? DateTime.MinValue))
            {
                if (string.IsNullOrWhiteSpace(c.Id)) { results.Add(new(c.Id ?? "", "error", "missing id")); continue; }
                if (done.TryGetValue(c.Id, out var prior)) { results.Add(new(c.Id, "duplicate", prior)); continue; }
                string status = "ok", message;
                try { message = await ApplyOneAsync(c); }
                catch (SkipException ex) { status = "skipped"; message = ex.Message; }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Offline change {Type} failed", c.Type);
                    results.Add(new(c.Id, "error", ex.Message));
                    continue;      // not recorded: the phone keeps it and tries again later
                }
                await _receipts.UpsertAsync(new SyncReceipt { ClientId = c.Id, Type = c.Type, Result = message });
                done[c.Id] = message;
                results.Add(new(c.Id, status, message));
            }
            // Keep the receipts for a month — far longer than any phone stays offline.
            await _receipts.DeleteManyAsync(_receipts.Where(r => r.CreatedUtc < DateTime.UtcNow.AddDays(-30)).Select(r => r.Id));
            return results;
        }
        finally { _one.Release(); }
    }

    private sealed class SkipException(string message) : Exception(message);

    private static string Str(JsonElement d, string name) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static Guid Id(JsonElement d) => Guid.TryParse(Str(d, "id"), out var g) ? g : throw new SkipException("unknown item");

    private async Task<string> ApplyOneAsync(Change c)
    {
        var d = c.Data;
        switch (c.Type)
        {
            case "task.add":
            {
                var title = Str(d, "title");
                if (title.Length == 0) throw new SkipException("empty");
                var cat = Str(d, "category");
                if (cat.Length == 0) cat = _settings.Current.General.TaskCategories.FirstOrDefault() ?? "Admin";
                DateOnly? due = DateOnly.TryParseExact(Str(d, "due"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd) ? dd : null;
                var t = await _tasks.QuickAddAsync(title, cat, due, "app");
                return $"task {t.Id}";
            }
            case "task.complete":
            {
                var id = Id(d);
                var t = _tasks.Repo.Get(id) ?? throw new SkipException("task no longer exists");
                if (!t.IsOpen) throw new SkipException("already done");
                await _tasks.CompleteAsync(id);
                return "completed";
            }
            case "shopping.add":
            {
                var name = Str(d, "name");
                if (name.Length == 0) throw new SkipException("empty");
                var qty = d.TryGetProperty("quantity", out var q) && q.TryGetDecimal(out var qv) && qv > 0 ? qv : 1;
                var item = await _kitchen.AddToShoppingAsync(name, qty, Str(d, "unit"), "App");
                return $"shopping {item.Id}";
            }
            case "shopping.check":
            {
                var item = _kitchen.Shopping.Get(Id(d)) ?? throw new SkipException("item no longer on the list");
                var want = !d.TryGetProperty("checked", out var ch) || ch.ValueKind != JsonValueKind.False;
                if (item.Checked == want) throw new SkipException("already so");
                item.Checked = want;
                await _kitchen.Shopping.UpsertAsync(item);
                return want ? "ticked" : "unticked";
            }
            case "note.add":
            {
                var text = Str(d, "text");
                if (text.Length == 0) throw new SkipException("empty");
                var n = await _notes.AddAsync(text, d.TryGetProperty("shared", out var sh) && sh.ValueKind == JsonValueKind.True, "app");
                return $"note {n.Id}";
            }
            default:
                throw new SkipException($"unknown change '{c.Type}' — update the app or LifeHub");
        }
    }
}
