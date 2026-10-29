using System.Text.Json;
using System.Text.Json.Serialization;

namespace LifeHub.Data;

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = indented };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static T Clone<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Compact), Compact)!;
}

/// <summary>
/// Thread-safe JSON file backed repository. Everything is held in memory and written
/// atomically (temp file + rename) on every change. Reads return copies, so a UI page
/// editing an object never changes stored data until it calls UpsertAsync.
/// </summary>
public sealed class JsonRepository<T> : IRepository<T>, IOwnedRepository where T : Entity
{
    /// <summary>Who may see these records (see <see cref="UserScope"/>).</summary>
    private static readonly OwnScope Scope = OwnedAttribute.Of(typeof(T));
    private static bool Visible(T x) => UserScope.CanSee(x, Scope);

    private readonly string _path;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _sync = new();
    private Dictionary<Guid, T> _items;

    public event Action? Changed;

    public JsonRepository(DataPaths paths, ILogger<JsonRepository<T>> log)
    {
        _log = log;
        _path = Path.Combine(paths.Db, typeof(T).Name + ".json");
        _items = Load();
    }

    private Dictionary<Guid, T> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            using var fs = File.OpenRead(_path);
            var list = JsonSerializer.Deserialize<List<T>>(fs, Json.Options) ?? new();
            return list.ToDictionary(x => x.Id);
        }
        catch (Exception ex)
        {
            // Never silently wipe data: keep the broken file for manual recovery.
            var broken = _path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(_path, broken, overwrite: true);
            _log.LogError(ex, "Could not read {Path}; a copy was saved as {Broken}", _path, broken);
            return new();
        }
    }

    public IReadOnlyList<T> All()
    {
        lock (_sync) return _items.Values.Where(Visible).Select(Json.Clone).ToList();
    }

    public T? Get(Guid id)
    {
        lock (_sync) return _items.TryGetValue(id, out var v) && Visible(v) ? Json.Clone(v) : null;
    }

    public IReadOnlyList<T> Where(Func<T, bool> predicate)
    {
        lock (_sync) return _items.Values.Where(x => Visible(x) && predicate(x)).Select(Json.Clone).ToList();
    }

    public Task UpsertAsync(T item) => UpsertManyAsync(new[] { item });

    public async Task UpsertManyAsync(IEnumerable<T> items)
    {
        lock (_sync)
        {
            foreach (var item in items)
            {
                if (_items.TryGetValue(item.Id, out var existing)) item.OwnerId = existing.OwnerId;   // edits never change the owner
                else UserScope.Stamp(item, Scope);
                item.UpdatedUtc = DateTime.UtcNow;
                _items[item.Id] = Json.Clone(item);
            }
        }
        await SaveAsync();
    }

    public Task DeleteAsync(Guid id) => DeleteManyAsync(new[] { id });

    public async Task DeleteManyAsync(IEnumerable<Guid> ids)
    {
        lock (_sync)
            foreach (var id in ids)
                if (_items.TryGetValue(id, out var v) && Visible(v)) _items.Remove(id);
        await SaveAsync();
    }

    public async Task<int> ForgetOwnerAsync(Guid owner, bool includeUnowned = false)
    {
        if (Scope == OwnScope.Global || owner == Guid.Empty) return 0;
        int n;
        lock (_sync)
        {
            var mine = _items.Values.Where(x => x.OwnerId == owner || (includeUnowned && Scope == OwnScope.Personal && x.OwnerId == Guid.Empty)).ToList();
            n = mine.Count;
            foreach (var x in mine)
            {
                if (Scope == OwnScope.Personal) _items.Remove(x.Id);
                else x.OwnerId = Guid.Empty;
            }
        }
        if (n > 0) await SaveAsync();
        return n;
    }

    private async Task SaveAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            List<T> snapshot;
            lock (_sync) snapshot = _items.Values.OrderBy(x => x.CreatedUtc).ToList();
            var tmp = _path + ".tmp";
            await using (var fs = File.Create(tmp))
                await JsonSerializer.SerializeAsync(fs, snapshot, Json.Options);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
        // Listeners (open pages) refresh as their own person, never as whoever made this change.
        using (UserScope.None())
            try { Changed?.Invoke(); } catch (Exception ex) { _log.LogWarning(ex, "Change listener failed"); }
    }
}

public static class JsonHttp
{
    /// <summary>
    /// Buffered JSON request body (sends Content-Length instead of chunked encoding, which some
    /// proxies and embedded servers mishandle).
    /// </summary>
    public static StringContent Body(object value) =>
        new(JsonSerializer.Serialize(value, value.GetType(), Json.Compact), System.Text.Encoding.UTF8, "application/json");
}
