using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Services;

/// <summary>
/// Quick notes: drop in a bit of text and LifeHub files it. The local AI picks a category (reusing your existing
/// ones where they fit), a few tags and a short heading. It never edits the note itself.
/// </summary>
public sealed class NotesService
{
    public const string Unsorted = "Unsorted";
    private readonly IRepository<Note> _repo;
    private readonly OllamaClient _ollama;
    private readonly SettingsService _settings;
    private readonly ILogger<NotesService> _log;
    private readonly ConcurrentDictionary<Guid, bool> _sorting = new();
    private readonly SemaphoreSlim _one = new(1, 1);

    public NotesService(IRepository<Note> repo, OllamaClient ollama, SettingsService settings, ILogger<NotesService> log)
    {
        _repo = repo; _ollama = ollama; _settings = settings; _log = log;
    }

    public IRepository<Note> Repo => _repo;
    public bool AiAvailable => _ollama.IsConfigured;

    /// <summary>Notes you can see: your own plus the ones others share. Pinned first, newest first.</summary>
    public IReadOnlyList<Note> All(bool archived = false) => _repo.Where(n => n.Archived == archived)
        .OrderByDescending(n => n.Pinned).ThenByDescending(n => n.CreatedUtc).ToList();

    public bool IsMine(Note n) => UserScope.UserId is { } me && UserScope.EffectiveOwner(n) == me;

    /// <summary>Category → number of notes, largest first (your own categories; shared notes keep their owner's).</summary>
    public IReadOnlyList<(string Name, int Count)> Categories() => All()
        .GroupBy(n => string.IsNullOrWhiteSpace(n.Category) ? Unsorted : n.Category)
        .Select(g => (g.Key, g.Count())).OrderBy(x => x.Key == Unsorted).ThenByDescending(x => x.Item2).ThenBy(x => x.Key).ToList();

    public async Task<Note> AddAsync(string text, bool shared = false, string source = "web")
    {
        text = text.Trim();
        if (text.Length == 0) throw new InvalidOperationException("Type something first");
        if (text.Length > 20000) text = text[..20000];
        var note = new Note
        {
            Text = text, Shared = shared, Source = source, Category = Guess(text, ExistingCategories()),
            AuthorName = _settings.Me?.FirstName ?? "", AiStatus = _ollama.IsConfigured ? "sorting" : ""
        };
        note.Tags = HashTags(text);
        await _repo.UpsertAsync(note);
        if (_ollama.IsConfigured) StartSort(note.Id);
        return note;
    }

    public bool IsSorting(Guid id) => _sorting.ContainsKey(id);

    /// <summary>Files one note in the background (as the person who added it).</summary>
    public void StartSort(Guid id)
    {
        if (!_sorting.TryAdd(id, true)) return;
        _ = Task.Run(async () =>
        {
            try { await SortAsync(id, CancellationToken.None); }
            catch (Exception ex)
            {
                _log.LogInformation("Note sorting failed: {Message}", ex.Message);
                if (_repo.Get(id) is { } n) { n.AiStatus = "failed"; n.AiError = ex.Message; await _repo.UpsertAsync(n); }
            }
            finally { _sorting.TryRemove(id, out _); }
        });
    }

    private List<string> ExistingCategories() => _repo.Where(n => !n.Archived && IsMine(n)).Select(n => n.Category)
        .Where(c => !string.IsNullOrWhiteSpace(c) && c != Unsorted).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private const string System = """
        You organise someone's quick notes. Put the note in ONE category.
        Rules:
        - Reuse an existing category whenever it reasonably fits (match its spelling exactly).
        - Only invent a new category if none fits. New categories are 1-3 words, Title Case, general enough for future notes
          (e.g. "Gift Ideas", "Recipes", "Home Projects", "Work", "Books & Films", "Prayer", "Health", "Shopping", "Ideas", "Links").
        - tags: 0-4 short lower-case keywords that would help find it later (people, places, topics). No duplicates of the category.
        - title: a short heading of at most 6 words summarising the note. Don't add facts that aren't in the note.
        Never rewrite the note.
        """;

    private static JsonObject OneSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["category"] = new JsonObject { ["type"] = "string" },
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["title"] = new JsonObject { ["type"] = "string" }
        },
        ["required"] = new JsonArray("category", "tags", "title")
    };

    public async Task SortAsync(Guid id, CancellationToken ct)
    {
        await _one.WaitAsync(ct);    // one at a time keeps a small Ollama machine responsive
        try
        {
            var note = _repo.Get(id);
            if (note is null) return;
            var existing = ExistingCategories();
            var user = new StringBuilder();
            user.AppendLine("EXISTING CATEGORIES: " + (existing.Count == 0 ? "(none yet)" : string.Join(", ", existing)));
            user.AppendLine();
            user.AppendLine("NOTE:");
            user.AppendLine(Clip(note.Text, 3000));
            var json = await _ollama.ChatJsonAsync(System, user.ToString(), OneSchema(), ct);
            note = _repo.Get(id);
            if (note is null) return;
            if (!note.CategoryLocked) note.Category = Tidy(Str(json, "category"), existing) ?? note.Category;
            note.Tags = MergeTags(note.Tags, json, note.Category);
            note.Title = Clip(Str(json, "title"), 60);
            note.AiStatus = "done"; note.AiError = "";
            await _repo.UpsertAsync(note);
        }
        finally { _one.Release(); }
    }

    private static JsonObject BatchSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["notes"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["n"] = new JsonObject { ["type"] = "integer" },
                        ["category"] = new JsonObject { ["type"] = "string" },
                        ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                        ["title"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("n", "category")
                }
            }
        },
        ["required"] = new JsonArray("notes")
    };

    /// <summary>
    /// "Tidy up": files all your notes again in batches, so similar ones end up together and near-duplicate
    /// categories ("Gift ideas" / "Presents") are merged. Notes whose category you set yourself are left alone.
    /// </summary>
    public async Task<(int Moved, int Categories)> TidyAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Turn on the local AI in Settings → AI to tidy notes up");
        var notes = _repo.Where(n => !n.Archived && IsMine(n) && !n.CategoryLocked).OrderBy(n => n.CreatedUtc).ToList();
        var locked = _repo.Where(n => !n.Archived && IsMine(n) && n.CategoryLocked).Select(n => n.Category).Distinct().ToList();
        var moved = 0;
        var known = new List<string>(locked);
        // Seed with the current categories so the first batch tends to keep them.
        known.AddRange(notes.Select(n => n.Category).Where(c => c.Length > 0 && c != Unsorted).Distinct(StringComparer.OrdinalIgnoreCase));
        const int batch = 12;
        for (var i = 0; i < notes.Count; i += batch)
        {
            var part = notes.Skip(i).Take(batch).ToList();
            progress?.Report($"Sorting notes {i + 1}–{i + part.Count} of {notes.Count}…");
            var sb = new StringBuilder();
            sb.AppendLine("Sort each note below into a category. Merge near-duplicate categories into one name.");
            sb.AppendLine("CATEGORIES SO FAR: " + (known.Count == 0 ? "(none)" : string.Join(", ", known.Distinct(StringComparer.OrdinalIgnoreCase))));
            sb.AppendLine();
            for (var k = 0; k < part.Count; k++) sb.AppendLine($"N{k + 1}: {Clip(part[k].Text, 500).Replace('\n', ' ')}");
            sb.AppendLine("\nReturn one entry per note with n = its number.");
            var json = await _ollama.ChatJsonAsync(System, sb.ToString(), BatchSchema(), ct);
            if (!json.TryGetProperty("notes", out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("n", out var nEl) || !nEl.TryGetInt32(out var n) || n < 1 || n > part.Count) continue;
                var note = _repo.Get(part[n - 1].Id);
                if (note is null || note.CategoryLocked) continue;
                var cat = Tidy(Str(item, "category"), known);
                if (cat is null) continue;
                if (!cat.Equals(note.Category, StringComparison.Ordinal)) moved++;
                note.Category = cat;
                note.Tags = MergeTags(note.Tags, item, cat);
                var title = Str(item, "title");
                if (title.Length > 0) note.Title = Clip(title, 60);
                note.AiStatus = "done";
                await _repo.UpsertAsync(note);
                if (!known.Contains(cat, StringComparer.OrdinalIgnoreCase)) known.Add(cat);
            }
        }
        return (moved, Categories().Count);
    }

    /// <summary>Puts a note in a category you choose; the AI won't move it again.</summary>
    public async Task MoveAsync(Guid id, string category)
    {
        var n = _repo.Get(id);
        if (n is null) return;
        category = Clean(category);
        n.Category = category.Length == 0 ? Unsorted : category;
        n.CategoryLocked = category.Length > 0;
        await _repo.UpsertAsync(n);
    }

    /// <summary>Renames a category (or merges it into another existing one) across your notes.</summary>
    public async Task<int> RenameCategoryAsync(string from, string to)
    {
        to = Clean(to);
        if (to.Length == 0) return 0;
        var list = _repo.Where(n => IsMine(n) && n.Category.Equals(from, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var n in list) { n.Category = to; n.CategoryLocked = true; }
        await _repo.UpsertManyAsync(list);
        return list.Count;
    }

    public async Task EditAsync(Guid id, string text)
    {
        var n = _repo.Get(id);
        if (n is null || string.IsNullOrWhiteSpace(text)) return;
        var changed = n.Text != text.Trim();
        n.Text = text.Trim();
        n.Tags = n.Tags.Union(HashTags(n.Text)).ToList();
        await _repo.UpsertAsync(n);
        if (changed && _ollama.IsConfigured && !n.CategoryLocked) StartSort(n.Id);
    }

    public async Task SetAsync(Guid id, Action<Note> change)
    {
        var n = _repo.Get(id);
        if (n is null) return;
        change(n);
        await _repo.UpsertAsync(n);
    }

    public IReadOnlyList<Note> Search(string query, string? category = null, bool archived = false)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return All(archived).Where(n =>
                (category is null || (category == "#shared" ? n.Shared : (string.IsNullOrWhiteSpace(n.Category) ? Unsorted : n.Category) == category)) &&
                words.All(w => n.Text.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Title.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                               n.Category.Contains(w, StringComparison.OrdinalIgnoreCase) || n.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    // ------------------------------------------------------------------ helpers

    private static readonly Regex Url = new(@"https?://\S+", RegexOptions.IgnoreCase);
    private static readonly Regex Hash = new(@"(?<!\w)#([\p{L}\d][\p{L}\d_-]{1,30})");

    /// <summary>Without the AI (or until it answers): a simple first guess.</summary>
    public static string Guess(string text, IReadOnlyCollection<string> existing)
    {
        var lower = text.ToLowerInvariant();
        foreach (var c in existing.OrderByDescending(c => c.Length))
            if (lower.Contains(c.ToLowerInvariant().TrimEnd('s'))) return c;
        string? pick =
            Url.IsMatch(text) && text.Length < 300 ? "Links" :
            Regex.IsMatch(lower, @"\b(gift|present|birthday|christmas)\b") ? "Gift Ideas" :
            Regex.IsMatch(lower, @"\b(recipe|cook|bake|ingredients?)\b") ? "Recipes" :
            Regex.IsMatch(lower, @"\b(pray|prayer|verse|sermon|church|bible)\b") ? "Faith" :
            Regex.IsMatch(lower, @"\b(book|film|movie|watch|read|series|podcast)\b") ? "Books & Films" :
            Regex.IsMatch(lower, @"\b(idea|maybe|what if)\b") ? "Ideas" : null;
        if (pick is null) return Unsorted;
        return existing.FirstOrDefault(e => e.Equals(pick, StringComparison.OrdinalIgnoreCase)) ?? pick;
    }

    private static List<string> HashTags(string text) =>
        Hash.Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct().Take(6).ToList();

    private static string Clean(string s)
    {
        s = Regex.Replace(s ?? "", @"\s+", " ").Trim().Trim('"', '\'', '.', '#');
        if (s.Length > 32) s = s[..32].Trim();
        return s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>Normalises an AI category and snaps it to an existing one that differs only in case or plural.</summary>
    private static string? Tidy(string raw, IReadOnlyCollection<string> existing)
    {
        var c = Clean(raw);
        if (c.Length == 0) return null;
        static string Key(string x) => Regex.Replace(x.ToLowerInvariant(), @"[^a-z0-9]", "").TrimEnd('s');
        return existing.FirstOrDefault(e => Key(e) == Key(c)) ?? c;
    }

    private static List<string> MergeTags(List<string> current, JsonElement json, string category)
    {
        var tags = new List<string>(current);
        if (json.TryGetProperty("tags", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var t in arr.EnumerateArray())
            {
                var tag = Regex.Replace((t.GetString() ?? "").ToLowerInvariant().Trim().TrimStart('#'), @"\s+", " ");
                if (tag.Length is > 0 and <= 24 && !tag.Equals(category, StringComparison.OrdinalIgnoreCase) && !tags.Contains(tag)) tags.Add(tag);
            }
        return tags.Take(6).ToList();
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
