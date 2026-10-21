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
    /// <summary>Uploaded Bible study notes (images, PDFs, documents).</summary>
    public string BibleFiles => Path.Combine(Root, "bible", "files");

    /// <summary>True when running inside a Home Assistant add-on container.</summary>
    public bool IsHomeAssistantAddon { get; }

    public DataPaths(IConfiguration config, IWebHostEnvironment env)
    {
        IsHomeAssistantAddon = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN"));
        var configured = Environment.GetEnvironmentVariable("LIFEHUB_DATA") ?? config["LifeHub:DataPath"];
        Root = !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(configured, env.ContentRootPath)
            : IsHomeAssistantAddon ? "/data" : Path.Combine(env.ContentRootPath, "data");

        foreach (var dir in new[] { Root, Db, Backups, Keys, BibleFiles }) Directory.CreateDirectory(dir);
    }
}
