namespace LifeHub.Mobile.Mobile;

/// <summary>A page to open because a notification was tapped (e.g. "inbox?tab=history").</summary>
public static class PendingNavigation
{
    public static event Action<string>? Requested;
    private static string? _waiting;

    public static void Request(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return;
        if (Requested is null) _waiting = link; else Requested(link);
    }

    public static string? Take() { var l = _waiting; _waiting = null; return l; }
}
