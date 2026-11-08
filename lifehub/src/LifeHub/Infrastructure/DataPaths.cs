namespace LifeHub.Infrastructure;

/// <summary>
/// Where LifeHub keeps its files. Resolution order: LIFEHUB_DATA env var,
/// "LifeHub:DataPath" config, /data when running as a Home Assistant add-on, else ./data.
/// </summary>
public sealed class DataPaths
{
    public string Root { get; }
    public string Db => Path.Combine(Root, "db");
    public string Backups => Path.Combine(Root, "backups");
    public string Keys => Path.Combine(Root, "keys");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    /// <summary>Optional stylesheet in the data folder, applied after the theme (for restyling LifeHub).</summary>
    public string CustomCssFile => Path.Combine(Root, "custom.css");
    /// <summary>Bible files from LifeHub 2.x (only used to export them; Bible features moved to their own app).</summary>
    public string BibleFiles => Path.Combine(Root, "bible", "files");

    /// <summary>True when running inside a Home Assistant add-on container.</summary>
    public bool IsHomeAssistantAddon { get; }

    /// <summary>A data folder chosen by the host (the phone app's private storage).</summary>
    public DataPaths(string root)
    {
        Root = root;
        foreach (var dir in new[] { Root, Db, Backups, Keys }) Directory.CreateDirectory(dir);
    }

#if !MOBILE
    public DataPaths(IConfiguration config, IWebHostEnvironment env)
    {
        IsHomeAssistantAddon = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN"));
        var configured = Environment.GetEnvironmentVariable("LIFEHUB_DATA") ?? config["LifeHub:DataPath"];
        Root = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured, env.ContentRootPath)
            : IsHomeAssistantAddon ? "/data" : Path.Combine(env.ContentRootPath, "data");

        foreach (var dir in new[] { Root, Db, Backups, Keys }) Directory.CreateDirectory(dir);
    }
#endif
}
