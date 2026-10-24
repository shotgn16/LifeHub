namespace LifeHub.Services;

public sealed record AppInterval(DateTime StartUtc, DateTime? EndUtc)
{
    public double Minutes(DateTime nowUtc) => ((EndUtc ?? nowUtc) - StartUtc).TotalMinutes;
}

/// <summary>
/// Turns Home Assistant history into Bible-app reading sessions.
///
/// The Android "Last used app" sensor keeps saying "Bible" after the phone is locked (until another app is
/// opened), so on its own it badly over-counts. When the phone's "Interactive" (screen on/off) sensor is
/// available, a session only counts while the Bible app is in front <b>and</b> the screen is on. Short glances
/// are dropped and brief switches away (a message, a notification) are merged back into one session.
/// </summary>
public static class BibleSessions
{
    public static bool IsBibleApp(string? state, string match)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        if (!string.IsNullOrWhiteSpace(match)) return state.Contains(match.Trim(), StringComparison.OrdinalIgnoreCase);
        return state.Contains("bible", StringComparison.OrdinalIgnoreCase) || state.Contains("youversion", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Unknown(string s) => s is "" or "unknown" or "unavailable";

    /// <param name="app">State changes of the "last used app" sensor (UTC, state), any order.</param>
    /// <param name="screen">State changes of the "interactive" sensor (on/off), or null when there isn't one.</param>
    /// <param name="minSeconds">Visits shorter than this are ignored (opened by accident, a quick glance).</param>
    /// <param name="mergeGapSeconds">Sessions closer together than this become one (popped out to reply to a message).</param>
    /// <param name="capMinutesWithoutScreen">Without a screen sensor, a session can't run longer than this.</param>
    public static List<AppInterval> Build(
        IEnumerable<(DateTime Utc, string State)> app,
        IEnumerable<(DateTime Utc, string State)>? screen,
        DateTime nowUtc, string match,
        int minSeconds = 45, int mergeGapSeconds = 120, int capMinutesWithoutScreen = 30)
    {
        var events = app.Select(a => (a.Utc, Kind: 'a', a.State))
            .Concat((screen ?? Enumerable.Empty<(DateTime, string)>()).Select(s => (s.Item1, Kind: 's', s.Item2)))
            .OrderBy(e => e.Item1).ThenBy(e => e.Kind)    // same instant: app change first, then screen
            .ToList();

        var hasScreen = screen is not null;
        bool appOn = false, screenOn = !hasScreen;
        DateTime? start = null;
        var raw = new List<AppInterval>();

        foreach (var (t, kind, state) in events)
        {
            if (t > nowUtc) break;
            if (kind == 'a')
            {
                if (Unknown(state)) continue;           // keep the last known app
                appOn = IsBibleApp(state, match);
            }
            else
            {
                if (Unknown(state)) continue;
                screenOn = state.Equals("on", StringComparison.OrdinalIgnoreCase);
            }
            var active = appOn && screenOn;
            if (active && start is null) start = t;
            else if (!active && start is { } s0) { raw.Add(new AppInterval(s0, t)); start = null; }
        }
        if (start is { } open) raw.Add(new AppInterval(open, null));

        // Without a screen sensor the app sensor can stay on "Bible" for hours after the phone is put down.
        if (!hasScreen)
            raw = raw.Select(i =>
            {
                var end = i.EndUtc ?? nowUtc;
                if ((end - i.StartUtc).TotalMinutes <= capMinutesWithoutScreen) return i;
                return new AppInterval(i.StartUtc, i.StartUtc.AddMinutes(capMinutesWithoutScreen));
            }).ToList();

        // Merge brief interruptions.
        var merged = new List<AppInterval>();
        foreach (var i in raw.OrderBy(i => i.StartUtc))
        {
            if (merged.Count > 0 && merged[^1].EndUtc is { } prevEnd && (i.StartUtc - prevEnd).TotalSeconds <= mergeGapSeconds)
                merged[^1] = new AppInterval(merged[^1].StartUtc, i.EndUtc);
            else merged.Add(i);
        }

        // Drop glances (an ongoing session is kept once it's long enough).
        return merged.Where(i => (((i.EndUtc ?? nowUtc) - i.StartUtc).TotalSeconds) >= minSeconds).ToList();
    }

    /// <summary>sensor.pixel_8_last_used_app → binary_sensor.pixel_8_interactive</summary>
    public static string? ScreenSensorFor(string appSensor)
    {
        const string suffix = "_last_used_app";
        if (!appSensor.StartsWith("sensor.") || !appSensor.EndsWith(suffix)) return null;
        var device = appSensor["sensor.".Length..^suffix.Length];
        return $"binary_sensor.{device}_interactive";
    }
}
