using System.IO.Compression;

namespace LifeHub.Services;

/// <summary>
/// Zips the (encrypted) db folder and settings.json into data/backups, keeping the latest 14.
/// The key is <em>not</em> included, so a backup copied elsewhere can't be read without it: this LifeHub opens
/// its own backups, and on another computer you need the recovery key (Settings → Backup).
/// </summary>
public sealed class BackupService
{
    private readonly DataPaths _paths;
    private readonly Clock _clock;

    private readonly SettingsService _settings;

    public BackupService(DataPaths paths, Clock clock, SettingsService settings)
    {
        _paths = paths; _clock = clock; _settings = settings;
    }

    public IReadOnlyList<FileInfo> List() => new DirectoryInfo(_paths.Backups).GetFiles("lifehub-2*.zip").OrderByDescending(f => f.Name).ToList();

    public string Create()
    {
        var file = Path.Combine(_paths.Backups, $"lifehub-{_clock.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var f in Directory.GetFiles(_paths.Db, "*.json")) zip.CreateEntryFromFile(f, "db/" + Path.GetFileName(f));
            if (File.Exists(_paths.SettingsFile)) zip.CreateEntryFromFile(_paths.SettingsFile, "settings.json");
            var readme = zip.CreateEntry("README.txt");
            using var w = new StreamWriter(readme.Open());
            w.Write("LifeHub backup made " + _clock.Now.ToString("d MMMM yyyy HH:mm") + ".\n" +
                    "The files are encrypted. To restore on the same LifeHub: stop it, unzip this into its data folder (replacing db/ and settings.json), start it.\n" +
                    "On another computer, also put your recovery key (Settings → Backup → Recovery key) into keys/recovery.key first.\n");
        }
        foreach (var old in List().Skip(Math.Clamp(_settings.Current.Background.KeepBackups, 1, 365))) old.Delete();
        return file;
    }

    public bool HasBackupToday() => List().Any(f => f.Name.StartsWith($"lifehub-{_clock.Now:yyyyMMdd}"));
}
