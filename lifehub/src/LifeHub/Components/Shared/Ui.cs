using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace LifeHub.Components.Shared;

/// <summary>Per-circuit UI helper: toasts and shared formatting.</summary>
public sealed class UiState
{
    public sealed record Toast(Guid Id, string Message, bool Error);
    private readonly List<Toast> _toasts = new();
    public IReadOnlyList<Toast> Toasts { get { lock (_toasts) return _toasts.ToArray(); } }
    public event Action? Changed;

    public void Show(string message, bool error = false)
    {
        var t = new Toast(Guid.NewGuid(), message, error);
        lock (_toasts) _toasts.Add(t);
        Changed?.Invoke();
        _ = Task.Delay(error ? 6000 : 3200).ContinueWith(_ =>
        {
            lock (_toasts) _toasts.Remove(t);
            Changed?.Invoke();
        });
    }

    public void Error(Exception ex) => Show(ex.Message, true);

    /// <summary>Runs an action and reports failures as a toast instead of breaking the circuit.</summary>
    public async Task Try(Func<Task> action, string? success = null)
    {
        try
        {
            await action();
            if (success is not null) Show(success);
        }
        catch (Exception ex) { Error(ex); }
    }
}

public static class Fmt
{
    public static CultureInfo Culture { get; set; } = CultureInfo.GetCultureInfo("en-GB");

    public static string Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3) currency = "GBP";
        var symbol = currency.ToUpperInvariant() switch { "GBP" => "£", "USD" => "$", "EUR" => "€", "AUD" => "A$", "CAD" => "C$", _ => "" };
        return symbol.Length > 0 ? symbol + amount.ToString("#,0.00", Culture) : $"{amount.ToString("#,0.00", Culture)} {currency}";
    }

    public static string RelDay(DateOnly date, DateOnly today)
    {
        var d = date.DayNumber - today.DayNumber;
        return d switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => date.ToString("dddd", Culture),
            < -1 => $"{-d} days ago",
            _ => date.ToString(date.Year == today.Year ? "ddd d MMM" : "d MMM yyyy", Culture)
        };
    }

    public static string DaysLeft(int? days) => days switch
    {
        null => "no date",
        < -1 => $"expired {-days}d ago",
        -1 => "expired yesterday",
        0 => "today",
        1 => "tomorrow",
        _ => $"{days} days"
    };

    public static string DaysClass(int? days, int warn = 3) => days switch
    {
        null => "",
        < 0 => "danger",
        _ when days <= 1 => "danger",
        _ when days <= warn => "warn",
        _ => "ok"
    };

    public static string Ago(DateTime utc)
    {
        var s = DateTime.UtcNow - utc;
        if (s.TotalMinutes < 1) return "just now";
        if (s.TotalMinutes < 60) return $"{(int)s.TotalMinutes}m ago";
        if (s.TotalHours < 24) return $"{(int)s.TotalHours}h ago";
        return $"{(int)s.TotalDays}d ago";
    }

    public static string Qty(decimal q, string unit) =>
        (q == Math.Floor(q) ? q.ToString("0", Culture) : q.ToString("0.##", Culture)) + (string.IsNullOrWhiteSpace(unit) ? "" : " " + unit);

    public static string Split(string pascal) =>
        System.Text.RegularExpressions.Regex.Replace(pascal, "(?<=[a-z])([A-Z])", " $1");
}

/// <summary>Base for pages that should re-render when repository data changes (e.g. from a background job or another tab).</summary>
public abstract class LiveComponent : ComponentBase, IDisposable
{
    private readonly List<Action> _unsubscribe = new();

    protected void Watch<T>(IRepository<T> repo) where T : Entity
    {
        // Changes can come from anyone's work; this page always reloads as its own person.
        var me = UserScope.Current;
        void Handler() => InvokeAsync(() => { using (UserScope.Use(me)) { OnDataChanged(); StateHasChanged(); } });
        repo.Changed += Handler;
        _unsubscribe.Add(() => repo.Changed -= Handler);
    }

    protected void Watch(SettingsService settings)
    {
        var me = UserScope.Current;
        void Handler() => InvokeAsync(() => { using (UserScope.Use(me)) { OnDataChanged(); StateHasChanged(); } });
        settings.Changed += Handler;
        _unsubscribe.Add(() => settings.Changed -= Handler);
    }

    /// <summary>Called on the render thread when watched data changes — reload local copies here.</summary>
    protected virtual void OnDataChanged() { }

    public virtual void Dispose()
    {
        foreach (var u in _unsubscribe) u();
        GC.SuppressFinalize(this);
    }
}
