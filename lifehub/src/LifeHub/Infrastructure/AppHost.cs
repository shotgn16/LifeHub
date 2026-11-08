namespace LifeHub.Infrastructure;

/// <summary>Which program LifeHub's pages are running in, for the few places that differ.</summary>
public static class AppHost
{
#if MOBILE
    /// <summary>True inside the phone app running on its own (no server, no sign-in).</summary>
    public const bool IsPhoneApp = true;
#else
    public const bool IsPhoneApp = false;
#endif
}
