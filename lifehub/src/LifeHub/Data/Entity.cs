namespace LifeHub.Data;

/// <summary>Base class for everything stored by the repository layer.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Storage abstraction. The default implementation keeps each collection in a JSON file,
/// which is plenty for a single-household app. Swap in an EF Core implementation by
/// registering your own IRepository&lt;T&gt; in Program.cs.
/// </summary>
public interface IRepository<T> where T : Entity
{
    IReadOnlyList<T> All();
    T? Get(Guid id);
    IReadOnlyList<T> Where(Func<T, bool> predicate);
    Task UpsertAsync(T item);
    Task UpsertManyAsync(IEnumerable<T> items);
    Task DeleteAsync(Guid id);
    Task DeleteManyAsync(IEnumerable<Guid> ids);

    /// <summary>Raised after any change so open UI pages can refresh.</summary>
    event Action? Changed;
}
