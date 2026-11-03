using System.Text.Json;

namespace LifeHub.Desktop;

/// <summary>How this computer runs LifeHub — chosen in the setup assistant, changeable in Settings → This computer.</summary>
public enum HostMode
{
    /// <summary>Only on this computer; LifeHub runs while the app is open.</summary>
    Local,
    /// <summary>This computer plus your phone (one person): runs in the background, reachable on your network.</summary>
    LocalAndPhone,
    /// <summary>A server for the household (several people and phones): starts with the computer.</summary>
    Server,
    /// <summary>LifeHub runs in Docker on this computer; the app is a window onto it.</summary>
    Docker,
    /// <summary>LifeHub runs somewhere else (Home Assistant, another computer); the app is a window onto it.</summary>
    Remote
}

public sealed class HostConfig
{
    public HostMode Mode { get; set; } = HostMode.Local;
    public int Port { get; set; } = 8099;
    /// <summary>For Docker / Remote: the LifeHub address to open.</summary>
    public string RemoteUrl { get; set; } = "";
    /// <summary>Run in the background and start with the computer (LocalAndPhone / Server).</summary>
    public bool Background { get; set; } = true;
    /// <summary>"user" (starts when you sign in) or "system" (starts with the computer, before anyone signs in).</summary>
    public string ServiceScope { get; set; } = "user";
    /// <summary>The first step of the setup assistant is done (how LifeHub runs here has been chosen).</summary>
    public bool ModeChosen { get; set; }
    public bool SetupDone { get; set; }

    /// <summary>Listens on the network (phones and other computers) rather than only this computer.</summary>
    public bool OnNetwork => Mode is HostMode.LocalAndPhone or HostMode.Server;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static string FileIn(string dataDir) => Path.Combine(dataDir, "desktop.json");

    public static HostConfig Load(string dataDir)
    {
        try
        {
            var f = FileIn(dataDir);
            if (File.Exists(f)) return JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(f), Options) ?? new();
        }
        catch { }
        return new HostConfig();
    }

    public void Save(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(FileIn(dataDir), JsonSerializer.Serialize(this, Options));
    }
}

/// <summary>Where the desktop app keeps LifeHub's data on each system (unless LIFEHUB_DATA or --data says otherwise).</summary>
public static class DesktopPaths
{
    public static string DefaultData()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LifeHub");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS()) return Path.Combine(home, "Library", "Application Support", "LifeHub");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, "lifehub");
    }

    /// <summary>The program to start for the background service (inside LifeHub.app on a Mac).</summary>
    public static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LifeHub.exe" : "LifeHub");
}
