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
public sealed class JsonRepository<T> : IRepository<T> where T : Entity
{
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
        lock (_sync) return _items.Values.Select(Json.Clone).ToList();
    }

    public T? Get(Guid id)
    {
        lock (_sync) return _items.TryGetValue(id, out var v) ? Json.Clone(v) : null;
    }

    public IReadOnlyList<T> Where(Func<T, bool> predicate)
    {
        lock (_sync) return _items.Values.Where(predicate).Select(Json.Clone).ToList();
    }

    public Task UpsertAsync(T item) => UpsertManyAsync(new[] { item });

    public async Task UpsertManyAsync(IEnumerable<T> items)
    {
        lock (_sync)
        {
            foreach (var item in items)
            {
                item.UpdatedUtc = DateTime.UtcNow;
                _items[item.Id] = Json.Clone(item);
            }
        }
        await SaveAsync();
    }

    public Task DeleteAsync(Guid id) => DeleteManyAsync(new[] { id });

    public async Task DeleteManyAsync(IEnumerable<Guid> ids)
    {
        lock (_sync) foreach (var id in ids) _items.Remove(id);
        await SaveAsync();
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
