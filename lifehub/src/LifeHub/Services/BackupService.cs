using System.IO.Compression;

namespace LifeHub.Services;

/// <summary>Zips the db folder, settings.json and the encryption keys into data/backups (keeps the latest 14).</summary>
public sealed class BackupService
{
    private readonly DataPaths _paths;
    private readonly Clock _clock;

    public BackupService(DataPaths paths, Clock clock)
    {
        _paths = paths; _clock = clock;
    }

    public IReadOnlyList<FileInfo> List() => new DirectoryInfo(_paths.Backups).GetFiles("lifehub-*.zip").OrderByDescending(f => f.Name).ToList();

    public string Create()
    {
        var file = Path.Combine(_paths.Backups, $"lifehub-{_clock.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var f in Directory.GetFiles(_paths.Db, "*.json")) zip.CreateEntryFromFile(f, "db/" + Path.GetFileName(f));
            if (File.Exists(_paths.SettingsFile)) zip.CreateEntryFromFile(_paths.SettingsFile, "settings.json");
            foreach (var f in Directory.GetFiles(_paths.Keys)) zip.CreateEntryFromFile(f, "keys/" + Path.GetFileName(f));
        }
        foreach (var old in List().Skip(14)) old.Delete();
        return file;
    }

    public bool HasBackupToday() => List().Any(f => f.Name.StartsWith($"lifehub-{_clock.Now:yyyyMMdd}"));
}
