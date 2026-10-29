using System.Reflection;
using System.Security.Claims;

namespace LifeHub.Data;

/// <summary>Who can see a kind of record when several people use LifeHub.</summary>
public enum OwnScope
{
    /// <summary>Everyone (product catalogue, sync bookkeeping, store deals).</summary>
    Global,
    /// <summary>Only the person who created it (tasks, email, Bible, notes…).</summary>
    Personal,
    /// <summary>Food stock, shopping list and purchase history — shared or personal per the household setting.</summary>
    Kitchen,
    /// <summary>Recipes and the meal plan — shared or personal per the household setting.</summary>
    Meals,
    /// <summary>Renewals and subscriptions — shared or personal per the household setting.</summary>
    Renewals
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class OwnedAttribute : Attribute
{
    public OwnedAttribute(OwnScope scope) => Scope = scope;
    public OwnScope Scope { get; }

    public static OwnScope Of(Type t) => t.GetCustomAttribute<OwnedAttribute>()?.Scope ?? OwnScope.Global;
}

/// <summary>A personal record its owner can choose to share with the whole household.</summary>
public interface IShareable
{
    bool Shared { get; }
}

/// <summary>How the household shares things, as last saved in Settings → People.</summary>
public sealed record HouseholdView(Guid Primary, bool Kitchen, bool Meals, bool Renewals, IReadOnlyList<Guid> Users)
{
    public bool Shares(OwnScope scope) => scope switch
    {
        OwnScope.Kitchen => Kitchen,
        OwnScope.Meals => Meals,
        OwnScope.Renewals => Renewals,
        OwnScope.Global => true,
        _ => false
    };
}

/// <summary>
/// The person LifeHub is currently working for. It flows with async code: the web request, the Blazor circuit
/// (set for every event and render by <c>UserCircuitHandler</c>) and background jobs (which run as
/// <see cref="AsSystem"/> and switch to each person with <see cref="As"/> when doing per-person work).
/// Repositories use it to decide what each person can see, so one person's emails and tasks never show for another.
/// With no scope at all (e.g. a change notification arriving from another person's work) personal data is hidden.
/// </summary>
public static class UserScope
{
    public const string ClaimUserId = "lh:uid";
    public const string ClaimVia = "lh:via";

    public sealed record State(Guid User, bool System);

    private static readonly AsyncLocal<State?> _current = new();

    public static HouseholdView Household { get; set; } = new(Guid.Empty, true, true, true, Array.Empty<Guid>());

    public static State? Current => _current.Value;
    /// <summary>The signed-in person, or null in background/system work.</summary>
    public static Guid? UserId => _current.Value is { System: false } s ? s.User : null;
    public static bool IsSystem => _current.Value?.System == true;

    public static IDisposable Use(State? state)
    {
        var previous = _current.Value;
        _current.Value = state;
        return new Restore(previous);
    }

    public static IDisposable As(Guid userId) => Use(new State(userId, false));
    public static IDisposable AsSystem() => Use(new State(Guid.Empty, true));
    public static IDisposable None() => Use(null);

    private sealed class Restore : IDisposable
    {
        private readonly State? _previous;
        private bool _done;
        public Restore(State? previous) => _previous = previous;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _current.Value = _previous;
        }
    }

    /// <summary>The LifeHub person behind a signed-in principal. Sign-ins from before 2.0 (no id) belong to the first person.</summary>
    public static Guid? FromPrincipal(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true) return null;
        var raw = user.FindFirst(ClaimUserId)?.Value;
        if (Guid.TryParse(raw, out var id) && (Household.Users.Count == 0 || Household.Users.Contains(id))) return id;
        if (raw is not null) return null;      // a removed person: not signed in any more
        return Household.Primary == Guid.Empty ? null : Household.Primary;
    }

    /// <summary>Whether the current person may see a record of this scope.</summary>
    public static bool CanSee(Entity e, OwnScope scope)
    {
        if (scope == OwnScope.Global) return true;
        var s = _current.Value;
        if (s is null) return false;
        if (s.System) return true;
        var h = Household;
        if (scope != OwnScope.Personal && h.Shares(scope)) return true;
        if (e.OwnerId == s.User) return true;
        if (e.OwnerId == Guid.Empty && s.User == h.Primary) return true;   // data from before 2.0 / from household-shared times
        return e is IShareable { Shared: true };
    }

    /// <summary>
    /// New records belong to whoever creates them. Shared areas (kitchen, meals, renewals when shared) stay
    /// household-owned, so turning sharing off later leaves the existing list with the main person.
    /// </summary>
    public static void Stamp(Entity e, OwnScope scope)
    {
        if (scope == OwnScope.Global || e.OwnerId != Guid.Empty) return;
        if (_current.Value is not { System: false } s) return;
        if (scope != OwnScope.Personal && Household.Shares(scope)) return;
        e.OwnerId = s.User;
    }

    /// <summary>The owner a record effectively belongs to (household records belong to the main person).</summary>
    public static Guid EffectiveOwner(Entity e) => e.OwnerId == Guid.Empty ? Household.Primary : e.OwnerId;
}
