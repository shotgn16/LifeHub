using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LifeHub.Integrations.Ollama;

namespace LifeHub.Services;

public sealed record RelatedStudy(BibleStudy Study, double Score, List<string> Reasons);
public sealed record SourceExcerpt(string Name, string Url, string Text, bool Readable, string Note);

/// <summary>
/// Bible studies: stores uploaded notes, reads them with the local AI (OCR), breaks them down, finds links
/// to your other studies, and — only when you ask — compares them with StudyLight / Biblia Graphia.
/// AI output is always stored separately from your own notes and never changes them.
/// </summary>
public sealed class BibleStudyService
{
    private readonly IRepository<BibleStudy> _studies;
    private readonly OllamaClient _ollama;
    private readonly SettingsService _settings;
    private readonly DataPaths _paths;
    private readonly IHttpClientFactory _http;
    private readonly BibleService _bible;
    private readonly ILogger<BibleStudyService> _log;
    private readonly ConcurrentDictionary<Guid, string> _running = new();

    public const long MaxUploadBytes = 30 * 1024 * 1024;

    public BibleStudyService(IRepository<BibleStudy> studies, OllamaClient ollama, SettingsService settings, DataPaths paths,
        IHttpClientFactory http, BibleService bible, ILogger<BibleStudyService> log)
    {
        _studies = studies; _ollama = ollama; _settings = settings; _paths = paths; _http = http; _bible = bible; _log = log;
    }

    public IRepository<BibleStudy> Repo => _studies;
    public bool AiAvailable => _ollama.IsConfigured;
    public bool HasVision => _ollama.HasVision;

    /// <summary>What the AI is doing for a study right now (null when idle).</summary>
    public string? Working(Guid id) => _running.TryGetValue(id, out var s) ? s : null;

    // ------------------------------------------------------------------ files

    public string FilePath(StudyAttachment a) => Path.Combine(_paths.BibleFiles, a.StoredName);

    public async Task<StudyAttachment> AddAttachmentAsync(BibleStudy study, string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var safeExt = Path.GetExtension(fileName).ToLowerInvariant();
        if (safeExt.Length > 6 || !Regex.IsMatch(safeExt, @"^\.[a-z0-9]+$")) safeExt = "";
        var att = new StudyAttachment
        {
            FileName = Path.GetFileName(fileName),
            ContentType = string.IsNullOrWhiteSpace(contentType) ? GuessType(fileName) : contentType,
        };
        att.StoredName = $"{study.Id:N}-{att.Id:N}{safeExt}";
        await using (var f = File.Create(FilePath(att)))
            await content.CopyToAsync(f, ct);
        att.Size = new FileInfo(FilePath(att)).Length;
        var fresh = _studies.Get(study.Id) ?? study;
        fresh.Attachments.Add(att);
        await _studies.UpsertAsync(fresh);
        study.Attachments = fresh.Attachments;
        return att;
    }

    public async Task RemoveAttachmentAsync(Guid studyId, Guid attachmentId)
    {
        var study = _studies.Get(studyId);
        var att = study?.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (study is null || att is null) return;
        try { File.Delete(FilePath(att)); } catch { }
        study.Attachments.Remove(att);
        study.ExtractedText = string.Join("\n\n", study.Attachments.Where(a => a.Text.Length > 0).Select(a => $"[{a.FileName}]\n{a.Text}"));
        await _studies.UpsertAsync(study);
    }

    public async Task DeleteStudyAsync(Guid id)
    {
        var study = _studies.Get(id);
        if (study is null) return;
        foreach (var a in study.Attachments) try { File.Delete(FilePath(a)); } catch { }
        await _studies.DeleteAsync(id);
    }

    private static string GuessType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", ".heic" => "image/heic",
        ".pdf" => "application/pdf", ".txt" => "text/plain", ".md" => "text/markdown",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        _ => "application/octet-stream"
    };

    // ------------------------------------------------------------------ AI processing

    /// <summary>Reads uploads (OCR), then breaks the study down. Runs in the background; watch AiStatus.</summary>
    public void StartProcessing(Guid id, bool reOcr = false)
    {
        if (!_running.TryAdd(id, "Starting…")) return;
        _ = Task.Run(async () =>
        {
            try { await ProcessAsync(id, reOcr, CancellationToken.None); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Study processing failed");
                if (_studies.Get(id) is { } s) { s.AiStatus = "failed"; s.AiError = ex.Message; await _studies.UpsertAsync(s); }
            }
            finally { _running.TryRemove(id, out _); }
        });
    }

    private async Task SetStatus(Guid id, string status)
    {
        _running[id] = status;
        if (_studies.Get(id) is { } s) { s.AiStatus = "working"; s.AiError = status; await _studies.UpsertAsync(s); }
    }

    public async Task ProcessAsync(Guid id, bool reOcr, CancellationToken ct)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var study = _studies.Get(id) ?? throw new InvalidOperationException("Study not found");
        var i = 0;
        foreach (var att in study.Attachments.ToList())
        {
            i++;
            if (!reOcr && att.TextSource != "none" && att.Text.Length > 0) continue;
            await SetStatus(id, $"Reading {att.FileName} ({i}/{study.Attachments.Count})…");
            var (text, source) = await ExtractAsync(att, ct);
            study = _studies.Get(id)!;
            var target = study.Attachments.FirstOrDefault(a => a.Id == att.Id);
            if (target is null) continue;
            target.Text = text;
            target.TextSource = source;
            await _studies.UpsertAsync(study);
        }
        study = _studies.Get(id)!;
        study.ExtractedText = string.Join("\n\n", study.Attachments.Where(a => a.Text.Length > 0).Select(a => $"[{a.FileName}]\n{a.Text}"));
        await _studies.UpsertAsync(study);

        await SetStatus(id, "Breaking down your notes…");
        study = _studies.Get(id)!;
        var all = AllText(study);
        if (all.Trim().Length < 10) throw new InvalidOperationException("There's no text to work with yet — type some notes or upload a photo/PDF of them.");
        study.Breakdown = await BreakdownAsync(study, all, ct);
        if (_ollama.HasEmbeddings)
        {
            await SetStatus(id, "Finding related studies…");
            try { study.Embedding = await _ollama.EmbedAsync(EmbeddingText(study), ct); }
            catch (Exception ex) { _log.LogInformation("Embedding failed: {Message}", ex.Message); }
        }
        study.AiStatus = "done";
        study.AiError = "";
        await _studies.UpsertAsync(study);
    }

    public static string AllText(BibleStudy s) =>
        $"{s.Notes}\n\n{s.ExtractedText}".Trim();

    private static string EmbeddingText(BibleStudy s) =>
        $"{s.Title}\nPassages: {string.Join(", ", s.Passages)}\n{s.Breakdown?.Summary}\n{string.Join("; ", s.Breakdown?.KeyPoints ?? new())}\n{AllText(s)}";

    /// <summary>Text from one upload: PDF text layer, OCR for photos/scans, or the file itself.</summary>
    public async Task<(string Text, string Source)> ExtractAsync(StudyAttachment att, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(FilePath(att), ct);
        var ext = Path.GetExtension(att.FileName).ToLowerInvariant();
        if (att.ContentType.StartsWith("text/") || ext is ".txt" or ".md")
            return (Encoding.UTF8.GetString(bytes).Trim(), "text");
        if (ext == ".docx") return (DocxText(bytes), "text");
        if (att.IsImage) return (await OcrAsync(new[] { bytes }, ct), "ocr");
        if (att.IsPdf)
        {
            var sb = new StringBuilder();
            var scanned = new List<byte[]>();
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(bytes))
            {
                foreach (var page in pdf.GetPages().Take(30))
                {
                    var t = PageText(page);
                    if (t.Length >= 40) { sb.AppendLine(t).AppendLine(); continue; }
                    foreach (var img in page.GetImages().Take(2))
                    {
                        if (img.TryGetPng(out var png)) scanned.Add(png);
                        else
                        {
                            var raw = img.RawMemory.ToArray();
                            if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8) scanned.Add(raw);   // JPEG scan
                        }
                    }
                }
            }
            if (scanned.Count > 0)
            {
                // One page at a time keeps small vision models accurate.
                foreach (var img in scanned.Take(15)) sb.AppendLine(await OcrAsync(new[] { img }, ct)).AppendLine();
                return (sb.ToString().Trim(), sb.Length > 0 && scanned.Count > 0 ? "ocr" : "pdf-text");
            }
            return (sb.ToString().Trim(), "pdf-text");
        }
        return ("", "none");
    }

    /// <summary>Rebuilds lines from word positions (page.Text runs lines together).</summary>
    private static string PageText(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
        if (words.Count == 0) return "";
        var lines = new List<List<UglyToad.PdfPig.Content.Word>>();
        foreach (var w in words.OrderByDescending(w => w.BoundingBox.Bottom).ThenBy(w => w.BoundingBox.Left))
        {
            var tolerance = Math.Max(2, w.BoundingBox.Height * 0.5);
            var line = lines.FirstOrDefault(l => Math.Abs(l[0].BoundingBox.Bottom - w.BoundingBox.Bottom) <= tolerance);
            if (line is null) lines.Add(new() { w }); else line.Add(w);
        }
        return string.Join("\n", lines.Select(l => string.Join(" ", l.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))).Trim();
    }

    private async Task<string> OcrAsync(IReadOnlyList<byte[]> images, CancellationToken ct)
    {
        const string system = "You transcribe photographs and scans of handwritten or printed Bible study notes.";
        const string prompt = """
            Transcribe ALL the text in this image exactly as written.
            - Keep the original line breaks, headings, bullet points and numbering.
            - Keep Bible references exactly as written (e.g. "Rom 8:28").
            - Write [?] for any word you cannot read. Do not guess whole sentences.
            - Do not add any commentary, explanation or summary. Output only the transcription.
            """;
        var text = await _ollama.ChatTextAsync(system, prompt, ct, images);
        return text.Trim().Trim('`').Trim();
    }

    private static string DocxText(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            var entry = zip.GetEntry("word/document.xml");
            if (entry is null) return "";
            using var r = new StreamReader(entry.Open());
            var xml = r.ReadToEnd();
            xml = Regex.Replace(xml, @"</w:p>", "\n");
            xml = Regex.Replace(xml, @"<w:tab/>", "\t");
            xml = Regex.Replace(xml, @"<[^>]+>", "");
            return WebUtility.HtmlDecode(xml).Trim();
        }
        catch { return ""; }
    }

    private async Task<StudyBreakdown> BreakdownAsync(BibleStudy study, string text, CancellationToken ct)
    {
        JsonObject Arr() => new() { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } };
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["summary"] = new JsonObject { ["type"] = "string" },
                ["key_points"] = Arr(), ["passages"] = Arr(), ["themes"] = Arr(), ["people"] = Arr(),
                ["questions"] = Arr(), ["applications"] = Arr()
            },
            ["required"] = new JsonArray("summary", "key_points", "passages", "themes", "people", "questions", "applications")
        };
        var system = """
            You help someone organise their own Bible study notes. Be faithful to what they wrote: don't add teaching,
            doctrine or conclusions that aren't in the notes. Use plain, warm, concise English.
            summary: 2–3 sentences on what the study covered and the main thought.
            key_points: the main points they made (max 8, short).
            passages: every Bible reference mentioned or clearly discussed, written like "Romans 8:28" or "Psalm 23".
            themes: 2–6 short topic words or phrases (e.g. "grace", "suffering and hope", "prayer").
            people: Bible people discussed (names only).
            questions: questions they raised, or that their notes leave open (max 5).
            applications: personal applications or prayer points they wrote down (max 5).
            Use [] for anything that isn't in the notes.
            """;
        var user = $"STUDY: {study.Title}\nDate: {study.Date:yyyy-MM-dd}\nPassages given: {string.Join(", ", study.Passages)}\n\nNOTES:\n{Trim(text, 12000)}";
        var json = await _ollama.ChatJsonAsync(system, user, schema, ct);
        List<string> L(string name) => json.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Distinct().Take(12).ToList()
            : new List<string>();
        var passages = BibleBooks.Normalise(L("passages").Concat(BibleBooks.FindInText(text).Select(r => r.ToString())));
        return new StudyBreakdown
        {
            Summary = json.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
            KeyPoints = L("key_points"), Passages = passages, Themes = L("themes").Select(t => t.ToLowerInvariant()).ToList(),
            People = L("people"), Questions = L("questions"), Applications = L("applications"),
            GeneratedUtc = DateTime.UtcNow, Model = _settings.Current.Ollama.Model
        };
    }

    // ------------------------------------------------------------------ links between studies

    public static List<BibleRef> RefsOf(BibleStudy s) =>
        s.Passages.Concat(s.Breakdown?.Passages ?? new()).SelectMany(BibleBooks.Parse).Distinct().ToList();

    /// <summary>Passages without the ones inside another (Romans 8:28 is part of Romans 8:28-39).</summary>
    public static List<BibleRef> MainRefsOf(BibleStudy s)
    {
        var all = RefsOf(s);
        return all.Where(r => !all.Any(o => !ReferenceEquals(o, r) && !o.Equals(r) && o.Span.From <= r.Span.From && o.Span.To >= r.Span.To)).ToList();
    }

    private static HashSet<string> ThemesOf(BibleStudy s) =>
        s.Tags.Concat(s.Breakdown?.Themes ?? new()).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 1).ToHashSet();

    public IReadOnlyList<RelatedStudy> Related(BibleStudy study, int max = 6)
    {
        var refs = RefsOf(study);
        var themes = ThemesOf(study);
        var people = (study.Breakdown?.People ?? new()).Select(p => p.ToLowerInvariant()).ToHashSet();
        var result = new List<RelatedStudy>();
        foreach (var other in _studies.All().Where(o => o.Id != study.Id))
        {
            double score = 0;
            var reasons = new List<string>();
            var otherRefs = RefsOf(other);
            foreach (var a in refs)
            {
                var overlap = otherRefs.FirstOrDefault(b => a.Overlaps(b));
                if (overlap is not null) { score += 3; reasons.Add($"Both study {(a.Span == overlap.Span ? a.ToString() : $"{a} / {overlap}")}"); continue; }
                var chapter = otherRefs.FirstOrDefault(b => a.SameChapterAs(b));
                if (chapter is not null) { score += 2; reasons.Add($"Same chapter: {a.BookInfo.Name} {a.Chapter}"); continue; }
                if (otherRefs.Any(b => b.Book == a.Book)) { score += 0.5; }
            }
            if (refs.Count > 0 && otherRefs.Any(b => refs.Any(a => a.Book == b.Book)) && !reasons.Any())
                reasons.Add($"Same book: {refs.First(a => otherRefs.Any(b => b.Book == a.Book)).BookInfo.Name}");
            var sharedThemes = themes.Intersect(ThemesOf(other)).ToList();
            if (sharedThemes.Count > 0) { score += 1.5 * sharedThemes.Count; reasons.Add("Shared theme: " + string.Join(", ", sharedThemes.Take(3))); }
            var sharedPeople = people.Intersect((other.Breakdown?.People ?? new()).Select(p => p.ToLowerInvariant())).ToList();
            if (sharedPeople.Count > 0) { score += sharedPeople.Count; reasons.Add("Also about " + string.Join(", ", sharedPeople.Take(3).Select(Title))); }
            if (study.Embedding is { } e1 && other.Embedding is { } e2)
            {
                var cos = OllamaClient.Cosine(e1, e2);
                if (cos >= 0.6) { score += (cos - 0.6) * 10; if (cos >= 0.75) reasons.Add($"Similar ideas ({cos:P0} match)"); }
            }
            if (score >= 1.5) result.Add(new RelatedStudy(other, score, reasons.Distinct().Take(3).ToList()));
        }
        return result.OrderByDescending(r => r.Score).Take(max).ToList();
    }

    private static string Title(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // ------------------------------------------------------------------ reference sites (only on request)

    public void StartInsights(Guid id)
    {
        if (!_running.TryAdd(id, "Looking things up…")) return;
        _ = Task.Run(async () =>
        {
            try { await InsightsAsync(id, CancellationToken.None); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Study insights failed");
                if (_studies.Get(id) is { } s) { s.AiStatus = "failed"; s.AiError = ex.Message; await _studies.UpsertAsync(s); }
            }
            finally { _running.TryRemove(id, out _); }
        });
    }

    public string Url(string template, BibleRef r) => template
        .Replace("{book}", r.BookInfo.Slug).Replace("{chapter}", r.Chapter.ToString())
        .Replace("{verse}", (r.Verse ?? 1).ToString()).Replace("{usfm}", r.Usfm)
        .Replace("{name}", Uri.EscapeDataString(r.BookInfo.Name));

    /// <summary>Links to the enabled reference sites for each passage (for the "Read more" list).</summary>
    public IReadOnlyList<(string Name, string Url, string Passage)> SourceLinks(BibleStudy study)
    {
        var s = _settings.Current.Bible;
        var list = new List<(string, string, string)>();
        foreach (var r in MainRefsOf(study).Take(4))
        {
            if (s.StudyLightEnabled) list.Add(("StudyLight", Url(s.StudyLightUrl, r), r.ToString()));
            if (s.BibliaGraphiaEnabled) list.Add(("Biblia Graphia", Url(s.BibliaGraphiaUrl, r), r.ToString()));
        }
        return list.DistinctBy(x => x.Item2).ToList();
    }

    public async Task<List<SourceExcerpt>> FetchSourcesAsync(BibleStudy study, CancellationToken ct)
    {
        var s = _settings.Current.Bible;
        var excerpts = new List<SourceExcerpt>();
        var client = _http.CreateClient("web");
        client.Timeout = TimeSpan.FromSeconds(25);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LifeHub/1.3; personal Bible study helper)");
        foreach (var r in MainRefsOf(study).Take(3))
        {
            var targets = new List<(string Name, string Url)>();
            if (s.StudyLightEnabled) targets.Add(("StudyLight", Url(s.StudyLightUrl, r)));
            if (s.BibliaGraphiaEnabled) targets.Add(("Biblia Graphia", Url(s.BibliaGraphiaUrl, r)));
            foreach (var (name, url) in targets)
            {
                if (excerpts.Any(e => e.Url == url)) continue;
                try
                {
                    var html = await client.GetStringAsync(url, ct);
                    var text = HtmlText.Extract(html, anchor: $"{r.BookInfo.Name} {r.Chapter}", maxChars: 7000);
                    var readable = text.Length >= 400;
                    excerpts.Add(new SourceExcerpt(name, url, readable ? text : "", readable,
                        readable ? r.ToString() : "The page didn't contain readable text (it may load its content with JavaScript)."));
                }
                catch (Exception ex)
                {
                    excerpts.Add(new SourceExcerpt(name, url, "", false, "Couldn't fetch: " + ex.Message));
                }
            }
        }
        return excerpts;
    }

    public async Task InsightsAsync(Guid id, CancellationToken ct)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var study = _studies.Get(id) ?? throw new InvalidOperationException("Study not found");
        if (study.Breakdown is null && AllText(study).Length > 10)
        {
            await ProcessAsync(id, false, ct);
            study = _studies.Get(id)!;
        }
        await SetStatus(id, "Reading StudyLight / Biblia Graphia…");
        var sources = await FetchSourcesAsync(study, ct);
        var related = Related(study, 4);

        await SetStatus(id, "Comparing with your notes…");
        // Keep the question within the model's context window so its answer isn't cut off.
        var budget = _ollama.InputCharBudget() - 2500;                 // system prompt + schema
        var notesBudget = Math.Max(1200, (int)(budget * 0.35));
        var sb = new StringBuilder();
        sb.AppendLine($"MY STUDY: {study.Title} ({study.Date:d MMM yyyy})");
        sb.AppendLine($"Passages: {string.Join(", ", RefsOf(study).Select(r => r.ToString()))}");
        sb.AppendLine("MY NOTES:");
        sb.AppendLine(Trim(AllText(study), notesBudget));
        if (study.Breakdown is { } b) sb.AppendLine($"\nThemes: {string.Join(", ", b.Themes)}\nMy questions: {string.Join(" | ", b.Questions)}");
        foreach (var r in related)
            sb.AppendLine($"\nMY OTHER STUDY \"{r.Study.Title}\" ({r.Study.Date:d MMM yyyy}; {string.Join(", ", r.Study.Passages)}): {Trim(r.Study.Breakdown?.Summary ?? r.Study.Notes, 300)}");
        var readable = sources.Where(x => x.Readable).ToList();
        if (readable.Count > 0)
        {
            var perSource = Math.Max(800, (budget - sb.Length) / readable.Count);
            foreach (var src in readable)
                sb.AppendLine($"\nSOURCE {src.Name} ({src.Url}):\n{Trim(src.Text, perSource)}");
        }

        var kinds = new JsonArray("connection", "expand", "context", "cross-reference", "question", "related-study");
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["insights"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = kinds },
                            ["title"] = new JsonObject { ["type"] = "string" },
                            ["body"] = new JsonObject { ["type"] = "string" },
                            ["source"] = new JsonObject { ["type"] = "string" }
                        },
                        ["required"] = new JsonArray("kind", "title", "body", "source")
                    }
                }
            },
            ["required"] = new JsonArray("insights")
        };
        var sourceNames = sources.Where(x => x.Readable).Select(x => x.Name).Distinct().ToList();
        var system = $"""
            You are a thoughtful Bible study companion. The user has written study notes. Help them go deeper — do NOT
            rewrite, correct or summarise their notes. Offer 4–8 short insights:
              connection = a link between their notes and something in a source, another passage or another of their studies
              expand = a point in their notes they could develop further, and how
              context = helpful historical, cultural or literary background
              cross-reference = other passages that shed light on this one (give references)
              question = a good question for further study or reflection
              related-study = a specific link to one of their other studies (name it)
            Each body: under 90 words, plain English. Respect that different Christian traditions read some things
            differently — say so rather than taking sides.
            source: exactly one of {string.Join(", ", sourceNames.Select(n => $"\"{n}\"").Append("\"Your study: <title>\"").Append("\"General knowledge\""))}.
            Only cite a site if the point really comes from its text below.
            """;
        var json = await _ollama.ChatJsonAsync(system, sb.ToString(), schema, ct);
        var insights = new List<AiInsight>();
        if (json.TryGetProperty("insights", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var x in arr.EnumerateArray())
            {
                string Get(string n) => x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
                var title = Get("title");
                var body = Get("body");
                if (title.Length == 0 && body.Length == 0) continue;
                var source = Get("source");
                var src = sources.FirstOrDefault(s => s.Readable && source.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
                var rel = related.FirstOrDefault(r => source.Contains(r.Study.Title, StringComparison.OrdinalIgnoreCase) ||
                                                      title.Contains(r.Study.Title, StringComparison.OrdinalIgnoreCase));
                insights.Add(new AiInsight
                {
                    Kind = Get("kind") is { Length: > 0 } k ? k : "connection",
                    Title = title, Body = body,
                    Source = src?.Name ?? (rel is not null ? $"Your study: {rel.Study.Title}" : source.Length > 0 ? source : "General knowledge"),
                    SourceUrl = src?.Url ?? "",
                    RelatedStudyId = rel?.Study.Id
                });
            }
        }
        study = _studies.Get(id)!;
        // Keep what you pinned, replace the rest.
        study.Insights = study.Insights.Where(i => i.Pinned).Concat(insights).ToList();
        study.AiStatus = "done";
        var unreadable = sources.Where(s => !s.Readable).Select(s => $"{s.Name}: {s.Note}").Distinct().ToList();
        study.AiError = unreadable.Count > 0 ? string.Join(" · ", unreadable) : "";
        await _studies.UpsertAsync(study);
    }

    // ------------------------------------------------------------------ go deeper (only on request)

    private static readonly HashSet<string> NotCommentators = new(StringComparer.OrdinalIgnoreCase)
        { "Greek", "Hebrew", "Aramaic", "Background", "Context", "Note", "Notes", "Summary", "Theme", "Themes", "Word", "Words", "Application", "Interpretation", "Historical", "Cultural", "Literary" };

    public static string LevelName(int level) => level switch { <= 1 => "Overview", 2 => "Detailed", _ => "In-depth study" };

    public void StartDeeper(Guid id)
    {
        if (!_running.TryAdd(id, "Getting ready to go deeper…")) return;
        _ = Task.Run(async () =>
        {
            try { await GoDeeperAsync(id, CancellationToken.None); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Go deeper failed");
                if (_studies.Get(id) is { } s) { s.AiStatus = "failed"; s.AiError = ex.Message; await _studies.UpsertAsync(s); }
            }
            finally { _running.TryRemove(id, out _); }
        });
    }

    /// <summary>Verses to read commentary on: spread through each passage (more at deeper levels).</summary>
    private static IEnumerable<BibleRef> CommentaryVerses(BibleRef r, int perPassage)
    {
        var first = r.Verse ?? 1;
        if (r.EndChapter is not null || r.EndVerse is null || perPassage <= 1) { yield return r with { Verse = first, EndVerse = null, EndChapter = null }; yield break; }
        var last = r.EndVerse.Value;
        var picks = Enumerable.Range(0, perPassage).Select(i => first + (int)Math.Round((last - first) * (double)i / Math.Max(1, perPassage - 1))).Distinct();
        foreach (var v in picks) yield return new BibleRef(r.Book, r.Chapter, v);
    }

    /// <summary>
    /// Go deeper: reads StudyLight's commentaries on the passage in small pieces (so it works on small machines),
    /// summarises each piece, then writes a deeper study — interpretations, context, word studies, what it could
    /// look like and how to apply it. Depth comes from Settings → Bible. Stored apart from your notes.
    /// </summary>
    public async Task GoDeeperAsync(Guid id, CancellationToken ct)
    {
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var study = _studies.Get(id) ?? throw new InvalidOperationException("Study not found");
        var s = _settings.Current.Bible;
        var level = Math.Clamp(s.DeeperLevel, 1, 3);
        var refs = MainRefsOf(study).Take(level == 3 ? 3 : 2).ToList();
        if (refs.Count == 0) throw new InvalidOperationException("Add the passage(s) you studied first.");

        // 1–2) Read StudyLight's commentaries in small pieces and summarise each one.
        var notes = await ReadCommentaryAsync(id, refs, level, s.DeeperWordStudies, ct);

        // 3) Write the deeper study.
        await SetStatus(id, $"Writing your {LevelName(level).ToLowerInvariant()}…");
        study = _studies.Get(id)!;
        var budget = _ollama.InputCharBudget() - 3500;
        var sb = new StringBuilder();
        sb.AppendLine($"PASSAGE(S): {string.Join("; ", refs)}");
        sb.AppendLine($"STUDY TITLE: {study.Title}");
        sb.AppendLine("MY NOTES (summary):");
        sb.AppendLine(study.Breakdown is { } b
            ? $"{b.Summary}\nKey points: {string.Join(" | ", b.KeyPoints)}\nMy questions: {string.Join(" | ", b.Questions)}"
            : Trim(AllText(study), Math.Max(1000, budget / 4)));
        if (notes.Count == 0) sb.AppendLine("\n(No commentary text was available — draw on widely held Christian interpretation and say that you are doing so.)");
        var per = notes.Count == 0 ? 0 : Math.Max(500, (budget - sb.Length) / notes.Count);
        foreach (var nn in notes) sb.AppendLine($"\nCOMMENTARY NOTES on {nn.Passage}:\n{Trim(nn.Notes, per)}");

        JsonObject Str() => new() { ["type"] = "string" };
        JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
        JsonObject Obj(params string[] fields)
        {
            var props = new JsonObject();
            foreach (var f in fields) props[f] = Str();
            return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()) };
        }
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["overview"] = Str(),
                ["interpretations"] = Arr(Obj("title", "explanation", "held_by")),
                ["context"] = Str(),
                ["words"] = Arr(Obj("word", "original", "meaning")),
                ["cross_references"] = Arr(Obj("reference", "why")),
                ["themes"] = Arr(Str()),
                ["looks_like"] = Arr(Obj("situation", "description")),
                ["applications"] = Arr(Obj("area", "practice")),
                ["questions"] = Arr(Str()),
                ["prayer"] = Str()
            },
            ["required"] = new JsonArray("overview", "interpretations", "context", "words", "cross_references", "themes", "looks_like", "applications", "questions", "prayer")
        };
        var depth = level switch
        {
            1 => "OVERVIEW: keep it short. 2 interpretations, 2 'looks like' examples, 2 applications, 1–2 sentences each. Context in 2 sentences.",
            2 => "DETAILED: 3 interpretations, 3 'looks like' examples, 3–4 applications, 3 cross-references, short paragraphs.",
            _ => "IN-DEPTH STUDY: 4–5 interpretations including where traditions differ, rich historical/literary context, 4 'looks like' scenarios, 5 applications, 5 cross-references, 5 questions."
        };
        var system = $"""
            You help someone go deeper in their own Bible study. Using the commentary notes (and their notes), explain:
              overview       = what the passage means at a deeper level (a short paragraph)
              interpretations = ways it can be understood more deeply; held_by = commentators or traditions (from the notes; "general" if not)
              context        = historical, cultural and literary background
              words          = {(s.DeeperWordStudies ? "key original-language words (word, original Greek/Hebrew with transliteration, meaning)" : "leave empty []")}
              cross_references = other passages that shed light on it, and why
              themes         = 2–5 theological themes
              looks_like     = what living this out could look like in real, everyday situations
              applications   = how it could be applied: area of life + a concrete practice{(string.IsNullOrWhiteSpace(s.DeeperFocus) ? "" : $" (focus on: {s.DeeperFocus})")}{(string.IsNullOrEmpty(study.StudyType) || study.StudyType == "personal" ? "" : $"\n              (This study is for {StudyTypes.Of(study.StudyType).Brief} — shape the applications and questions for that.)")}
              questions      = {(s.DeeperReflection ? "reflection / discussion questions" : "leave empty []")}
              prayer         = {(s.DeeperReflection ? "a short prayer based on the passage" : "leave empty \"\"")}
            {depth}
            Attribute views fairly; where Christians differ, say so without taking sides. Never invent quotes or
            commentators. Plain, warm UK English.
            """;
        var json = await _ollama.ChatJsonAsync(system, sb.ToString(), schema, ct);

        string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        List<string> L(string n) => json.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).ToList() : new();
        List<DeepItem> Items(string n, string t, string bd, string note = "") => json.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                .Select(x => new DeepItem { Title = S(x, t), Body = S(x, bd), Note = note.Length > 0 ? S(x, note) : "" })
                .Where(i => i.Title.Length > 0 || i.Body.Length > 0).ToList()
            : new();

        var deep = new DeepStudy
        {
            Level = level, GeneratedUtc = DateTime.UtcNow, Model = _settings.Current.Ollama.Model,
            Passages = refs.Select(r => r.ToString()).ToList(),
            Overview = S(json, "overview"),
            Interpretations = Items("interpretations", "title", "explanation", "held_by"),
            Context = S(json, "context"),
            Words = s.DeeperWordStudies ? Items("words", "word", "meaning", "original") : new(),
            CrossReferences = Items("cross_references", "reference", "why"),
            Themes = L("themes"),
            LooksLike = Items("looks_like", "situation", "description"),
            Applications = Items("applications", "area", "practice"),
            Questions = s.DeeperReflection ? L("questions") : new(),
            Prayer = s.DeeperReflection ? S(json, "prayer") : "",
            Sources = SourcesOf(notes)
        };
        if (deep.Overview.Length == 0 && deep.Interpretations.Count == 0) throw new AiFormatException("The AI didn't return a usable study. Please try again.");
        study = _studies.Get(id)!;
        study.Deeper = deep;
        study.AiStatus = "done";
        study.AiError = notes.Count == 0 && s.StudyLightEnabled ? "StudyLight couldn't be read just now, so this uses general knowledge only." : "";
        await _studies.UpsertAsync(study);
    }

    /// <summary>
    /// Reads StudyLight's commentary pages for the passages (more pages at deeper levels), in pieces small enough
    /// for an 8 GB machine, and summarises each piece into short study notes attributed to their commentators.
    /// </summary>
    private async Task<List<(string Passage, string Url, string Notes)>> ReadCommentaryAsync(Guid id, List<BibleRef> refs, int level,
        bool wordStudies, CancellationToken ct)
    {
        var s = _settings.Current.Bible;
        var perPassage = level switch { 1 => 1, 2 => 2, _ => 3 };
        var maxChunks = level switch { 1 => 2, 2 => 4, _ => 8 };
        var pageChars = level switch { 1 => 6000, 2 => 9000, _ => 14000 };
        const int chunkChars = 3000;
        var chunks = new List<(string Passage, string Url, string Text)>();
        if (s.StudyLightEnabled)
        {
            var client = _http.CreateClient("web");
            client.Timeout = TimeSpan.FromSeconds(25);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LifeHub/2.0; personal Bible study helper)");
            var pages = refs.SelectMany(r => CommentaryVerses(r, perPassage)).DistinctBy(v => v.Usfm).Take(level == 3 ? 6 : 3).ToList();
            var n = 0;
            foreach (var v in pages)
            {
                n++;
                await SetStatus(id, $"Reading StudyLight on {v} ({n}/{pages.Count})…");
                var url = Url(s.StudyLightUrl, v);
                try
                {
                    var html = await client.GetStringAsync(url, ct);
                    var text = HtmlText.Extract(html, anchor: $"{v.BookInfo.Name} {v.Chapter}", maxChars: pageChars);
                    if (text.Length < 400) continue;
                    var perPage = (int)Math.Ceiling(maxChunks / (double)pages.Count);
                    for (int i = 0, taken = 0; i < text.Length && taken < perPage; i += chunkChars, taken++)
                        chunks.Add((v.ToString(), url, text.Substring(i, Math.Min(chunkChars, text.Length - i))));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogInformation("StudyLight fetch failed for {Url}: {Message}", url, ex.Message); }
            }
            chunks = chunks.Take(maxChunks).ToList();
        }

        var notes = new List<(string Passage, string Url, string Notes)>();
        var k = 0;
        foreach (var c in chunks)
        {
            k++;
            await SetStatus(id, $"Studying the commentaries ({k}/{chunks.Count})…");
            var summary = await _ollama.ChatTextAsync(
                "You extract study notes from Bible commentary text. Use only what the text says; never invent quotes.",
                $"""
                PASSAGE: {c.Passage}
                COMMENTARY TEXT (StudyLight, several commentators — each section starts with the commentary's name):
                {c.Text}

                Write concise study notes (max {(level == 3 ? 220 : 160)} words) as short bullet points covering:
                - how each commentator interprets the passage (start the bullet with their name, e.g. "Barnes:")
                - historical, cultural or literary background they give
                {(wordStudies ? "- original-language (Greek/Hebrew) words they explain, with meanings\n" : "")}- cross-references they mention
                If the text isn't about this passage, reply only: NONE
                """, ct);
            summary = summary.Trim();
            if (summary.Length > 20 && !summary.StartsWith("NONE", StringComparison.OrdinalIgnoreCase))
                notes.Add((c.Passage, c.Url, summary));
        }
        return notes;
    }

    private static List<DeepItem> SourcesOf(List<(string Passage, string Url, string Notes)> notes) =>
        notes.GroupBy(n => n.Url).Select(g => new DeepItem
        {
            Title = $"StudyLight · {g.First().Passage}", Note = g.Key,
            Body = string.Join(", ", Regex.Matches(string.Join("\n", g.Select(x => x.Notes)), @"^\s*[-•*]?\s*([A-Z][\w'’.\- ]{2,40}?):", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value.Trim())
                .Where(n => !NotCommentators.Contains(n) && !n.StartsWith("Cross", StringComparison.OrdinalIgnoreCase)).Distinct().Take(6))
        }).ToList();

    // ------------------------------------------------------------------ Start a study from a verse

    /// <summary>Creates a new study for a verse and starts writing its starter notes in the background.</summary>
    public async Task<BibleStudy> CreateStarterAsync(string reference, string type, int level, string focus)
    {
        var refs = BibleBooks.Parse(reference);
        if (refs.Count == 0) throw new InvalidOperationException("That doesn't look like a Bible reference — try e.g. John 15:1-8 or Psalm 23.");
        if (!_ollama.IsConfigured) throw new InvalidOperationException("Set up the local AI (Ollama) in Settings → AI first.");
        var kind = StudyTypes.Of(type);
        var study = new BibleStudy
        {
            Date = DateOnly.FromDateTime(DateTime.Now), StudyType = kind.Key,
            Title = $"{string.Join("; ", refs)} · {kind.Name}",
            Passages = refs.Select(r => r.ToString()).ToList(),
            Tags = new() { kind.Name.ToLowerInvariant() }
        };
        await _studies.UpsertAsync(study);
        StartStarter(study.Id, kind.Key, level, focus);
        return study;
    }

    public void StartStarter(Guid id, string type, int level, string focus)
    {
        if (!_running.TryAdd(id, "Getting your starter notes ready…")) return;
        _ = Task.Run(async () =>
        {
            try { await StarterAsync(id, type, level, focus, CancellationToken.None); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Starter notes failed");
                if (_studies.Get(id) is { } s) { s.AiStatus = "failed"; s.AiError = ex.Message; await _studies.UpsertAsync(s); }
            }
            finally { _running.TryRemove(id, out _); }
        });
    }

    /// <summary>
    /// Starter notes: from the passage text (YouVersion), StudyLight's commentaries and the local AI, a first set of
    /// notes shaped by what the study is for — a home group, a sermon, a lesson, a youth session — at the chosen level.
    /// </summary>
    public async Task StarterAsync(Guid id, string type, int level, string focus, CancellationToken ct)
    {
        var study = _studies.Get(id) ?? throw new InvalidOperationException("Study not found");
        var kind = StudyTypes.Of(type);
        level = Math.Clamp(level, 1, 3);
        var refs = MainRefsOf(study).Take(2).ToList();
        if (refs.Count == 0) throw new InvalidOperationException("Add the passage first.");

        await SetStatus(id, "Getting the passage…");
        var passageText = new StringBuilder();
        foreach (var r in refs)
        {
            var t = await _bible.PassageTextAsync(r, ct);
            if (t.Length > 0) passageText.AppendLine($"{r}: {Trim(t, 2500)}");
        }

        var notes = await ReadCommentaryAsync(id, refs, level, wordStudies: level >= 2 || kind.Key == "word", ct);

        await SetStatus(id, $"Writing your {kind.Name.ToLowerInvariant()} notes…");
        var budget = _ollama.InputCharBudget() - 3500;
        var sb = new StringBuilder();
        sb.AppendLine($"PASSAGE: {string.Join("; ", refs)}");
        sb.AppendLine(passageText.Length > 0 ? $"TEXT:\n{passageText}" : "(Passage text not available — work from the reference.)");
        if (!string.IsNullOrWhiteSpace(focus)) sb.AppendLine($"WHAT I NEED: {focus.Trim()}");
        if (notes.Count == 0) sb.AppendLine("\n(No commentary text was available — draw on widely held Christian interpretation and say so.)");
        var per = notes.Count == 0 ? 0 : Math.Max(500, (budget - sb.Length) / notes.Count);
        foreach (var nn in notes) sb.AppendLine($"\nCOMMENTARY NOTES on {nn.Passage}:\n{Trim(nn.Notes, per)}");

        JsonObject Str() => new() { ["type"] = "string" };
        JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
        JsonObject Obj(params string[] fields)
        {
            var props = new JsonObject();
            foreach (var f in fields) props[f] = Str();
            return new JsonObject { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()) };
        }
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["big_idea"] = Str(),
                ["summary"] = Str(),
                ["context"] = Str(),
                ["sections"] = Arr(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["heading"] = Str(), ["points"] = Arr(Obj("title", "detail", "note")) },
                    ["required"] = new JsonArray("heading", "points")
                }),
                ["questions"] = Arr(Obj("question", "kind")),
                ["prayer"] = Str()
            },
            ["required"] = new JsonArray("big_idea", "summary", "context", "sections", "questions", "prayer")
        };
        var depth = level switch
        {
            1 => "LEVEL: getting started. Plain everyday language, no jargon, short points (1–2 sentences), 2–3 points per section, 4–5 questions. Assume no theological background.",
            2 => "LEVEL: going further. Clear language, some background and one or two original-language words where they help, 3–4 points per section, 6–8 questions.",
            _ => "LEVEL: in depth. Careful exegesis, Greek/Hebrew terms with transliteration, where interpreters differ (and who), more cross-references, 4–5 points per section, 8–10 questions."
        };
        var system = $"""
            You write STARTER NOTES for {kind.Brief}. The person will build on them with their own study,
            so be accurate, faithful to the text and practical.
            Return:
              big_idea  = the main point of the passage in one memorable sentence
              summary   = what the passage is about (2–3 sentences)
              context   = where it sits in the book, who wrote it, to whom and why (historical/literary background)
              sections  = exactly these sections, in this order: {kind.Sections}
                          Each section has points: title (short), detail (the substance), note (verse reference, timing or idea — or "")
              questions = good questions; kind is one of: {kind.QuestionKinds}
              prayer    = a short prayer (or prayer points for a group) based on the passage
            {depth}
            Base interpretation on the passage and the commentary notes. Where Christians differ, say so fairly without
            taking sides. Never invent quotes, statistics or commentators. Plain, warm UK English.
            """;
        var json = await _ollama.ChatJsonAsync(system, sb.ToString(), schema, ct);

        string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        var sections = new List<StarterSection>();
        if (json.TryGetProperty("sections", out var secs) && secs.ValueKind == JsonValueKind.Array)
            foreach (var sec in secs.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                var section = new StarterSection { Heading = S(sec, "heading") };
                if (sec.TryGetProperty("points", out var pts) && pts.ValueKind == JsonValueKind.Array)
                    section.Points = pts.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                        .Select(x => new DeepItem { Title = S(x, "title"), Body = S(x, "detail"), Note = S(x, "note") })
                        .Where(x => x.Title.Length > 0 || x.Body.Length > 0).ToList();
                if (section.Heading.Length > 0 && section.Points.Count > 0) sections.Add(section);
            }
        var questions = json.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array
            ? qs.EnumerateArray().Select(q => q.ValueKind == JsonValueKind.String
                    ? new DeepItem { Title = q.GetString()!.Trim() }
                    : new DeepItem { Title = S(q, "question"), Note = S(q, "kind").ToLowerInvariant() })
                .Where(q => q.Title.Length > 0).ToList()
            : new List<DeepItem>();

        var starter = new StudyStarter
        {
            Type = kind.Key, Level = level, Focus = focus.Trim(), GeneratedUtc = DateTime.UtcNow, Model = _settings.Current.Ollama.Model,
            Passage = string.Join("; ", refs), PassageText = passageText.ToString().Trim(),
            BigIdea = S(json, "big_idea"), Summary = S(json, "summary"), Context = S(json, "context"),
            Sections = sections, Questions = questions, Prayer = S(json, "prayer"), Sources = SourcesOf(notes)
        };
        if (starter.Sections.Count == 0 && starter.Summary.Length == 0) throw new AiFormatException("The AI didn't return usable notes. Please try again.");
        study = _studies.Get(id)!;
        study.Starter = starter;
        study.StudyType = kind.Key;
        study.AiStatus = "done";
        study.AiError = notes.Count == 0 && _settings.Current.Bible.StudyLightEnabled ? "StudyLight couldn't be read just now, so these notes use general knowledge only." : "";
        await _studies.UpsertAsync(study);
    }

    public async Task UpdateInsightAsync(Guid studyId, Guid insightId, Action<AiInsight> change)
    {
        var study = _studies.Get(studyId);
        var insight = study?.Insights.FirstOrDefault(i => i.Id == insightId);
        if (study is null || insight is null) return;
        change(insight);
        await _studies.UpsertAsync(study);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Very small HTML → readable text for reference pages.</summary>
public static class HtmlText
{
    public static string Extract(string html, string? anchor = null, int maxChars = 8000)
    {
        var s = Regex.Replace(html, @"<(script|style|noscript|svg|nav|header|footer|form)[^>]*>[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<(br|/p|/div|/h[1-6]|/li|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t\r\f\v]+", " ");
        s = Regex.Replace(s, @"\n\s*\n+", "\n\n").Trim();
        if (!string.IsNullOrEmpty(anchor))
        {
            // Skip menus: start at the first real mention of the passage after the page header.
            var idx = s.IndexOf(anchor, Math.Min(300, s.Length), StringComparison.OrdinalIgnoreCase);
            if (idx > 0) s = s[idx..];
        }
        return s.Length <= maxChars ? s : s[..maxChars];
    }
}
