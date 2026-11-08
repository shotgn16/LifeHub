namespace LifeHub.Mobile.Mobile;

public enum MobileMode
{
    /// <summary>Not chosen yet (first start).</summary>
    None,
    /// <summary>LifeHub runs on this phone: one person, everything stored (encrypted) on the phone.</summary>
    Device,
    /// <summary>A window onto your LifeHub server (signed in with your API key), with an offline copy.</summary>
    Server
}

/// <summary>How this app is set up. Kept in the phone's app preferences; the API key in its secure storage.</summary>
public sealed class MobileState
{
    public event Action? ModeChanged;

    public MobileMode Mode
    {
        get => Enum.TryParse<MobileMode>(Preferences.Default.Get("mode", ""), out var m) ? m : MobileMode.None;
        set { Preferences.Default.Set("mode", value.ToString()); ModeChanged?.Invoke(); }
    }

    public string ServerUrl
    {
        get => Preferences.Default.Get("server", "");
        set => Preferences.Default.Set("server", value.Trim().TrimEnd('/'));
    }

    public string ApiKey
    {
        get { try { return Task.Run(() => SecureStorage.Default.GetAsync("apikey")).GetAwaiter().GetResult() ?? ""; } catch { return ""; } }
        set { try { Task.Run(() => SecureStorage.Default.SetAsync("apikey", value.Trim())).GetAwaiter().GetResult(); } catch { } }
    }

    /// <summary>How often the phone checks in the background (minutes; 0 = never). Android can't go below 15.</summary>
    public int BackgroundMinutes
    {
        get => Preferences.Default.Get("bgMinutes", 15);
        set { Preferences.Default.Set("bgMinutes", value <= 0 ? 0 : Math.Max(15, value)); PhoneJobs.Schedule(this); }
    }

    /// <summary>Check email (and the server's notifications) whenever the app is opened.</summary>
    public bool CheckOnOpen
    {
        get => Preferences.Default.Get("checkOnOpen", true);
        set => Preferences.Default.Set("checkOnOpen", value);
    }

    /// <summary>When the server's notifications were last fetched (server mode).</summary>
    public DateTime NoticesSinceUtc
    {
        get => DateTime.TryParse(Preferences.Default.Get("noticesSince", ""), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.UtcNow.AddHours(-1);
        set => Preferences.Default.Set("noticesSince", value.ToString("o"));
    }
}
