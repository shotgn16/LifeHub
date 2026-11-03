using System.IO.Compression;
using System.Text.Json.Nodes;

namespace LifeHub.Services;

/// <summary>
/// LifeHub 3 no longer has Bible features (they moved to their own app). Data left from LifeHub 2.x — readings,
/// app sessions, verses, plans, studies and uploaded study files — can be exported to a zip and then deleted.
/// Until then it's left untouched (LifeHub simply doesn't read it).
/// </summary>
public sealed class BibleArchiveService
{
    private static readonly string[] Files = { "BibleReading", "BibleAppSession", "DailyVerse", "ReadingPlanItem", "BibleStudy" };
    private readonly DataPaths _paths;
    private readonly SettingsService _settings;
    private readonly Clock _clock;

    public BibleArchiveService(DataPaths paths, SettingsService settings, Clock clock)
    {
        _paths = paths; _settings = settings; _clock = clock;
    }

    private string BibleDir => Path.Combine(_paths.Root, "bible");
    private IEnumerable<string> DbFiles => Files.Select(f => Path.Combine(_paths.Db, f + ".json")).Where(File.Exists);

    public bool Exists => DbFiles.Any() || Directory.Exists(BibleDir);

    public IReadOnlyList<FileInfo> Exports() => new DirectoryInfo(_paths.Backups).GetFiles("bible-export-*.zip").OrderByDescending(f => f.Name).ToList();

    /// <summary>Creates bible-export-….zip in the backups folder (data, uploaded files and the old Bible settings).</summary>
    public string Export()
    {
        var file = Path.Combine(_paths.Backups, $"bible-export-{_clock.Now:yyyyMMdd-HHmmss}.zip");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            foreach (var f in DbFiles) zip.CreateEntryFromFile(f, "db/" + Path.GetFileName(f));
            if (Directory.Exists(BibleDir))
                foreach (var f in Directory.GetFiles(BibleDir, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(f, "bible/" + Path.GetRelativePath(BibleDir, f).Replace('\\', '/'));
            // The Bible part of the old settings (household + each person's), without anything else.
            if (File.Exists(_paths.SettingsFile))
            {
                var root = JsonNode.Parse(File.ReadAllText(_paths.SettingsFile)) as JsonObject;
                var keep = new JsonObject();
                if (root?["bible"] is JsonNode household) keep["household"] = household.DeepClone();
                var people = new JsonArray();
                foreach (var u in (root?["users"] as JsonArray) ?? new JsonArray())
                    if (u?["bible"] is JsonNode b) people.Add(new JsonObject { ["person"] = u["name"]?.DeepClone(), ["bible"] = b.DeepClone() });
                keep["people"] = people;
                var entry = zip.CreateEntry("bible-settings.json");
                using var w = new StreamWriter(entry.Open());
                w.Write(keep.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            var readme = zip.CreateEntry("README.txt");
            using (var w = new StreamWriter(readme.Open()))
                w.Write("Bible data exported from LifeHub on " + _clock.Now.ToString("d MMMM yyyy HH:mm") +
                        ".\ndb/*.json: readings, app sessions, verses of the day, reading plan and studies (JSON).\n" +
                        "bible/files: uploaded study notes (photos, PDFs, documents).\nbible-settings.json: the old Bible settings.\n");
        }
        return file;
    }

    /// <summary>Deletes the Bible data (only after an export exists).</summary>
    public async Task<int> DeleteAsync()
    {
        if (Exports().Count == 0) throw new InvalidOperationException("Export the Bible data first.");
        var n = 0;
        foreach (var f in DbFiles.ToList()) { File.Delete(f); n++; }
        if (Directory.Exists(BibleDir)) { n += Directory.GetFiles(BibleDir, "*", SearchOption.AllDirectories).Length; Directory.Delete(BibleDir, true); }
        // Saving settings rewrites settings.json without the old Bible sections.
        await _settings.UpdateAsync(_ => { });
        return n;
    }
}
