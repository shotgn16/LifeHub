using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LifeHub.Integrations.HomeAssistant;

/// <summary>Remembers which HA to-do item belongs to which LifeHub item, and what each side looked like last sync.</summary>
public sealed class HaListLink : Entity
{
    /// <summary>"shopping" or "tasks"</summary>
    public string List { get; set; } = "";
    public string TodoEntity { get; set; } = "";
    public string Uid { get; set; } = "";
    public Guid LocalId { get; set; }
    public string LastRemoteSummary { get; set; } = "";
    public bool LastRemoteDone { get; set; }
    public string LastLocalHash { get; set; } = "";
    public bool LastLocalDone { get; set; }
}

/// <summary>
/// Two-way sync between LifeHub and HA to-do lists, so HA's own Shopping List (Assist voice, the HA app,
/// dashboards) and LifeHub's shopping list are the same list — and optionally the same for tasks.
/// Runs every minute, and a few seconds after anything changes on the LifeHub side.
/// </summary>
public sealed class HaListSyncService : BackgroundService
{
    private const int SetDueDate = 16, SetDueDateTime = 32, SetDescription = 64;
    private static readonly Regex QtySuffix = new(@"^(?<name>.+?)\s*(?:\((?<q>\d+(?:\.\d+)?)\)|[x×]\s*(?<q>\d+(?:\.\d+)?))\s*$", RegexOptions.IgnoreCase);

    private readonly HomeAssistantClient _ha;
    private readonly SettingsService _settings;
    private readonly IRepository<HaListLink> _links;
    private readonly KitchenService _kitchen;
    private readonly TaskService _tasks;
    private readonly ILogger<HaListSyncService> _log;
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly SemaphoreSlim _running = new(1, 1);
    private volatile bool _syncing;

    public string LastResult { get; private set; } = "";
    public DateTime? LastRunUtc { get; private set; }

    public HaListSyncService(HomeAssistantClient ha, SettingsService settings, IRepository<HaListLink> links,
        KitchenService kitchen, TaskService tasks, ILogger<HaListSyncService> log)
    {
        _ha = ha; _settings = settings; _links = links; _kitchen = kitchen; _tasks = tasks; _log = log;
        kitchen.Shopping.Changed += Poke;
        tasks.Repo.Changed += Poke;
    }

    private void Poke()
    {
        if (_syncing) return; // our own writes
        if (_signal.CurrentCount == 0) try { _signal.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var system = UserScope.AsSystem();
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        while (!ct.IsCancellationRequested)
        {
            await SyncNowAsync(ct);
            await _signal.WaitAsync(TimeSpan.FromSeconds(60), ct);
            await Task.Delay(TimeSpan.FromSeconds(3), ct); // let a burst of edits settle
        }
    }

    public async Task<string> SyncNowAsync(CancellationToken ct = default)
    {
        // The shopping list is the household's (the main person's when food isn't shared); each person can link
        // their own HA to-do list for their tasks.
        var shoppingEntity = _settings.Household.HomeAssistant.ShoppingTodoEntity;
        var people = _settings.Users.Where(u => !string.IsNullOrEmpty(u.TasksTodoEntity)).ToList();
        if (!_ha.IsConfigured || (string.IsNullOrEmpty(shoppingEntity) && people.Count == 0)) return LastResult = "Not set up";
        if (!await _running.WaitAsync(0, ct)) return LastResult;
        _syncing = true;
        try
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(shoppingEntity))
                using (UserScope.As(_settings.PrimaryUserId))
                    parts.Add("shopping: " + await SyncShoppingAsync(shoppingEntity, ct));
            foreach (var person in people)
                using (UserScope.As(person.Id))
                    parts.Add($"tasks{(people.Count > 1 ? " (" + person.FirstName + ")" : "")}: " + await SyncTasksAsync(person.TasksTodoEntity, ct));
            LastRunUtc = DateTime.UtcNow;
            return LastResult = string.Join("; ", parts);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "HA list sync failed");
            return LastResult = "Error: " + ex.Message;
        }
        finally
        {
            _syncing = false;
            _running.Release();
        }
    }

    // ------------------------------------------------------------------ HA to-do helpers

    private sealed record RemoteItem(string Uid, string Summary, bool Done, string? Due, string? Description);

    private async Task<List<RemoteItem>> GetItemsAsync(string entity, CancellationToken ct)
    {
        var resp = await _ha.CallServiceWithResponseAsync("todo", "get_items", new { entity_id = entity }, ct);
        if (!resp.TryGetProperty(entity, out var list) || !list.TryGetProperty("items", out var items)) return new();
        return items.EnumerateArray().Select(i => new RemoteItem(
            i.GetProperty("uid").GetString() ?? "",
            i.GetProperty("summary").GetString() ?? "",
            i.TryGetProperty("status", out var st) && st.GetString() == "completed",
            i.TryGetProperty("due", out var due) && due.ValueKind == JsonValueKind.String ? due.GetString() : null,
            i.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null)).ToList();
    }

    private async Task<int> FeaturesAsync(string entity) =>
        (await _ha.GetStateAsync(entity)) is { } st && int.TryParse(st.Attr("supported_features"), out var f) ? f : 0;

    private Task SetStatusAsync(string entity, string uid, bool done) =>
        _ha.CallServiceAsync("todo", "update_item", new Dictionary<string, object>
            { ["entity_id"] = entity, ["item"] = uid, ["status"] = done ? "completed" : "needs_action" });

    // ------------------------------------------------------------------ shopping

    private static string ShoppingSummary(ShoppingItem i) =>
        i.Quantity == 1 ? i.Name : $"{i.Name} ({(i.Quantity == Math.Floor(i.Quantity) ? i.Quantity.ToString("0") : i.Quantity.ToString("0.##", CultureInfo.InvariantCulture))})";

    private static (string Name, decimal Qty) ParseSummary(string summary)
    {
        var m = QtySuffix.Match(summary.Trim());
        return m.Success && decimal.TryParse(m.Groups["q"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var q) && q > 0
            ? (m.Groups["name"].Value.Trim(), q) : (summary.Trim(), 1);
    }

    private async Task<string> SyncShoppingAsync(string entity, CancellationToken ct)
    {
        int added = 0, pushed = 0, updated = 0, removed = 0;
        var remote = await GetItemsAsync(entity, ct);
        var links = _links.Where(l => l.List == "shopping" && l.TodoEntity == entity).ToDictionary(l => l.Uid);
        var local = _kitchen.Shopping.All().ToDictionary(i => i.Id);
        var linkedLocal = links.Values.Select(l => l.LocalId).ToHashSet();
        var saveLinks = new List<HaListLink>();
        var deleteLinks = new List<Guid>();

        foreach (var r in remote)
        {
            if (links.TryGetValue(r.Uid, out var link))
            {
                if (!local.TryGetValue(link.LocalId, out var item))
                {
                    // Removed in LifeHub (deleted or put away) → remove from HA.
                    await _ha.CallServiceAsync("todo", "remove_item", new { entity_id = entity, item = r.Uid });
                    deleteLinks.Add(link.Id); removed++;
                    continue;
                }
                var changed = false;
                if (r.Done != link.LastRemoteDone) { item.Checked = r.Done; changed = true; }                       // ticked in HA
                else if (item.Checked != link.LastLocalDone) { await SetStatusAsync(entity, r.Uid, item.Checked); }  // ticked in LifeHub
                if (r.Summary != link.LastRemoteSummary)                                                              // renamed in HA
                {
                    var (name, qty) = ParseSummary(r.Summary);
                    item.Name = name; item.Quantity = qty; changed = true;
                }
                else if (ShoppingSummary(item) != link.LastLocalHash)                                                 // changed in LifeHub
                {
                    await _ha.CallServiceAsync("todo", "update_item", new { entity_id = entity, item = r.Uid, rename = ShoppingSummary(item) });
                }
                if (changed) { await _kitchen.Shopping.UpsertAsync(item); updated++; }
                link.LastRemoteSummary = ShoppingSummary(item);
                link.LastLocalHash = ShoppingSummary(item);
                link.LastRemoteDone = link.LastLocalDone = item.Checked;
                saveLinks.Add(link);
            }
            else
            {
                // New in HA (Assist, HA app…) — or first sync: match an unlinked LifeHub item by name.
                var (name, qty) = ParseSummary(r.Summary);
                var match = local.Values.FirstOrDefault(i => !linkedLocal.Contains(i.Id) && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    if (r.Done) continue; // old ticked items in HA aren't worth importing
                    // One HA item = one LifeHub item (no merging), so the two lists stay identical.
                    var staple = _kitchen.Staples.All().FirstOrDefault(st => st.Matches(name));
                    match = new ShoppingItem
                    {
                        Name = name, Quantity = qty, Reason = "Home Assistant", StapleId = staple?.Id,
                        Unit = staple?.Unit ?? "", Category = staple?.Category ?? "Other"
                    };
                    await _kitchen.Shopping.UpsertAsync(match);
                    local[match.Id] = match;
                    added++;
                }
                linkedLocal.Add(match.Id);
                if (match.Checked != r.Done) await SetStatusAsync(entity, r.Uid, match.Checked);
                saveLinks.Add(new HaListLink
                {
                    List = "shopping", TodoEntity = entity, Uid = r.Uid, LocalId = match.Id,
                    LastRemoteSummary = r.Summary, LastLocalHash = ShoppingSummary(match), LastRemoteDone = match.Checked, LastLocalDone = match.Checked
                });
            }
        }

        // Gone from HA → remove from LifeHub (but keep ticked items so "Put away" still works).
        var remoteUids = remote.Select(r => r.Uid).ToHashSet();
        foreach (var link in links.Values.Where(l => !remoteUids.Contains(l.Uid)))
        {
            if (local.TryGetValue(link.LocalId, out var item) && !item.Checked) { await _kitchen.Shopping.DeleteAsync(item.Id); removed++; }
            deleteLinks.Add(link.Id);
        }

        // New in LifeHub → add to HA.
        var toPush = _kitchen.Shopping.Where(i => !i.Checked && !linkedLocal.Contains(i.Id) && !saveLinks.Any(l => l.LocalId == i.Id));
        foreach (var item in toPush)
        {
            await _ha.CallServiceAsync("todo", "add_item", new { entity_id = entity, item = ShoppingSummary(item) });
            pushed++;
        }
        if (pushed > 0)
        {
            var after = await GetItemsAsync(entity, ct);
            var known = saveLinks.Select(l => l.Uid).ToHashSet();
            foreach (var item in toPush)
            {
                var r = after.FirstOrDefault(x => !known.Contains(x.Uid) && !links.ContainsKey(x.Uid) && x.Summary == ShoppingSummary(item));
                if (r is null) continue;
                known.Add(r.Uid);
                saveLinks.Add(new HaListLink
                {
                    List = "shopping", TodoEntity = entity, Uid = r.Uid, LocalId = item.Id,
                    LastRemoteSummary = r.Summary, LastLocalHash = ShoppingSummary(item)
                });
            }
        }

        if (deleteLinks.Count > 0) await _links.DeleteManyAsync(deleteLinks);
        if (saveLinks.Count > 0) await _links.UpsertManyAsync(saveLinks);
        return $"{remote.Count} in HA, +{added} from HA, +{pushed} to HA, {updated} updated, {removed} removed";
    }

    // ------------------------------------------------------------------ tasks

    private static string TaskHash(TaskItem t) => $"{t.Title}|{t.DueDate}|{t.DueTime}|{t.Notes}";

    private async Task<string> SyncTasksAsync(string entity, CancellationToken ct)
    {
        int added = 0, pushed = 0, done = 0, removed = 0;
        var features = await FeaturesAsync(entity);
        var remote = await GetItemsAsync(entity, ct);
        var links = _links.Where(l => l.List == "tasks" && l.TodoEntity == entity).ToDictionary(l => l.Uid);
        var local = _tasks.Repo.All().ToDictionary(t => t.Id);
        var linkedLocal = links.Values.Select(l => l.LocalId).ToHashSet();
        var saveLinks = new List<HaListLink>();
        var deleteLinks = new List<Guid>();
        var defaultCategory = _settings.Current.General.TaskCategories.FirstOrDefault() ?? "Admin";

        Dictionary<string, object> DueFields(TaskItem t)
        {
            var d = new Dictionary<string, object>();
            if (t.DueDate is { } dd)
            {
                if (t.DueTime is { } tm && (features & SetDueDateTime) != 0) d["due_datetime"] = dd.ToDateTime(tm).ToString("yyyy-MM-dd HH:mm:ss");
                else if ((features & SetDueDate) != 0) d["due_date"] = dd.ToString("yyyy-MM-dd");
            }
            if ((features & SetDescription) != 0 && !string.IsNullOrWhiteSpace(t.Notes)) d["description"] = t.Notes;
            return d;
        }

        foreach (var r in remote)
        {
            if (links.TryGetValue(r.Uid, out var link))
            {
                if (!local.TryGetValue(link.LocalId, out var t))
                {
                    await _ha.CallServiceAsync("todo", "remove_item", new { entity_id = entity, item = r.Uid });
                    deleteLinks.Add(link.Id); removed++;
                    continue;
                }
                var localDone = !t.IsOpen;
                if (r.Done && !link.LastRemoteDone && t.IsOpen) { await _tasks.CompleteAsync(t.Id); localDone = true; done++; }  // ticked in HA
                else if (!r.Done && link.LastRemoteDone && !t.IsOpen) { await _tasks.ReopenAsync(t.Id); localDone = false; }    // unticked in HA
                else if (localDone != link.LastLocalDone || localDone != r.Done) await SetStatusAsync(entity, r.Uid, localDone);   // changed in LifeHub
                t = _tasks.Repo.Get(t.Id) ?? t;
                if (r.Summary != link.LastRemoteSummary && r.Summary != t.Title)                                                    // renamed in HA
                {
                    t.Title = r.Summary;
                    await _tasks.SaveAsync(t);
                }
                else if (TaskHash(t) != link.LastLocalHash)                                                                          // edited in LifeHub
                {
                    var data = DueFields(t);
                    data["entity_id"] = entity; data["item"] = r.Uid; data["rename"] = t.Title;
                    await _ha.CallServiceAsync("todo", "update_item", data);
                }
                link.LastRemoteSummary = t.Title; link.LastLocalHash = TaskHash(t);
                link.LastRemoteDone = link.LastLocalDone = localDone;
                saveLinks.Add(link);
            }
            else
            {
                var match = local.Values.FirstOrDefault(t => t.IsOpen && !linkedLocal.Contains(t.Id) && t.Title.Equals(r.Summary.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    if (r.Done) continue;
                    match = new TaskItem { Title = r.Summary.Trim(), Notes = r.Description ?? "", Category = defaultCategory, Source = "homeassistant" };
                    if (r.Due is { } due)
                    {
                        if (DateTime.TryParse(due, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                        {
                            match.DueDate = DateOnly.FromDateTime(dt);
                            if (due.Contains('T') || due.Contains(' ')) match.DueTime = TimeOnly.FromDateTime(dt);
                        }
                    }
                    await _tasks.SaveAsync(match);
                    added++;
                }
                linkedLocal.Add(match.Id);
                saveLinks.Add(new HaListLink
                {
                    List = "tasks", TodoEntity = entity, Uid = r.Uid, LocalId = match.Id,
                    LastRemoteSummary = r.Summary, LastLocalHash = TaskHash(match), LastRemoteDone = r.Done, LastLocalDone = !match.IsOpen
                });
            }
        }

        var remoteUids = remote.Select(r => r.Uid).ToHashSet();
        foreach (var link in links.Values.Where(l => !remoteUids.Contains(l.Uid)))
        {
            // Deleted in HA → delete the open task in LifeHub (completed ones are kept as history).
            if (local.TryGetValue(link.LocalId, out var t) && t.IsOpen) { await _tasks.DeleteAsync(t.Id); removed++; }
            deleteLinks.Add(link.Id);
        }

        var toPush = _tasks.Repo.Where(t => t.IsOpen && !linkedLocal.Contains(t.Id)).ToList();
        foreach (var t in toPush)
        {
            var data = DueFields(t);
            data["entity_id"] = entity; data["item"] = t.Title;
            await _ha.CallServiceAsync("todo", "add_item", data);
            pushed++;
        }
        if (pushed > 0)
        {
            var after = await GetItemsAsync(entity, ct);
            var known = saveLinks.Select(l => l.Uid).Concat(links.Keys).ToHashSet();
            foreach (var t in toPush)
            {
                var r = after.FirstOrDefault(x => !known.Contains(x.Uid) && x.Summary == t.Title);
                if (r is null) continue;
                known.Add(r.Uid);
                saveLinks.Add(new HaListLink
                {
                    List = "tasks", TodoEntity = entity, Uid = r.Uid, LocalId = t.Id, LastRemoteSummary = t.Title, LastLocalHash = TaskHash(t)
                });
            }
        }

        if (deleteLinks.Count > 0) await _links.DeleteManyAsync(deleteLinks);
        if (saveLinks.Count > 0) await _links.UpsertManyAsync(saveLinks);
        return $"{remote.Count} in HA, +{added} from HA, +{pushed} to HA, {done} completed in HA, {removed} removed";
    }
}
