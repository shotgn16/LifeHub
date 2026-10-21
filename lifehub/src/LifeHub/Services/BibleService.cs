using System.Net.Http.Headers;
using System.Text.Json;

namespace LifeHub.Services;

public sealed record BibleDay(DateOnly Date, int Readings, double AppMinutes, int Studies, bool VerseRead)
{
    /// <summary>0 nothing · 1 opened the app briefly · 2 read · 3 studied</summary>
    public int Level(int minMinutes) => Studies > 0 ? 3 : Readings > 0 || VerseRead || AppMinutes >= minMinutes ? 2 : AppMinutes > 0 ? 1 : 0;
}

/// <summary>
/// Daily Bible reading: verse of the day (YouVersion Platform API when a key is set), the reading log,
/// time in the Bible app, carrying missed readings over to the next day and an optional reading plan.
/// </summary>
public sealed class BibleService
{
    private readonly IRepository<BibleReading> _readings;
    private readonly IRepository<BibleAppSession> _sessions;
    private readonly IRepository<DailyVerse> _verses;
    private readonly IRepository<ReadingPlanItem> _plan;
    private readonly IRepository<BibleStudy> _studies;
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly Clock _clock;
    private readonly ILogger<BibleService> _log;
    private readonly SemaphoreSlim _rollLock = new(1, 1);
    private DateTime _lastVerseAttemptUtc = DateTime.MinValue;

    public BibleService(IRepository<BibleReading> readings, IRepository<BibleAppSession> sessions, IRepository<DailyVerse> verses,
        IRepository<ReadingPlanItem> plan, IRepository<BibleStudy> studies, SettingsService settings, IHttpClientFactory http,
        Clock clock, ILogger<BibleService> log)
    {
        _readings = readings; _sessions = sessions; _verses = verses; _plan = plan; _studies = studies;
        _settings = settings; _http = http; _clock = clock; _log = log;
    }

    public IRepository<BibleReading> Readings => _readings;
    public IRepository<BibleAppSession> Sessions => _sessions;
    public IRepository<DailyVerse> Verses => _verses;
    public IRepository<ReadingPlanItem> Plan => _plan;
    public IRepository<BibleStudy> Studies => _studies;

    private BibleSettings S => _settings.Current.Bible;

    public DailyVerse? VerseFor(DateOnly date) => _verses.Where(v => v.Date == date).FirstOrDefault();

    public string LinkFor(string reference)
    {
        var r = BibleBooks.Parse(reference).FirstOrDefault();
        return r is null ? "https://www.bible.com/" : $"https://www.bible.com/bible/{S.VersionId}/{r.Usfm}";
    }

    // ------------------------------------------------------------------ verse of the day

    public bool HasYouVersion => !string.IsNullOrWhiteSpace(S.YouVersionAppKey);

    private HttpClient YouVersion()
    {
        var c = _http.CreateClient("youversion");
        c.BaseAddress = new Uri(S.YouVersionApiBase.TrimEnd('/') + "/");
        c.Timeout = TimeSpan.FromSeconds(20);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        c.DefaultRequestHeaders.Add("X-YVP-App-Key", S.YouVersionAppKey);
        return c;
    }

    /// <summary>Fetches the verse of the day (same as the YouVersion app) and its text.</summary>
    public async Task<DailyVerse> FetchVerseAsync(DateOnly date, CancellationToken ct = default)
    {
        if (!HasYouVersion) throw new InvalidOperationException("Add a YouVersion app key in Settings → Bible first.");
        using var c = YouVersion();
        using var r = await c.GetAsync($"v1/verse-of-the-days/{date.DayOfYear}", ct);
        var body = await r.Content.ReadAsStringAsync(ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"YouVersion returned {(int)r.StatusCode}: {body[..Math.Min(200, body.Length)]}");
        var usfm = JsonDocument.Parse(body).RootElement.GetProperty("passage_id").GetString() ?? "";
        var parsed = BibleRef.FromUsfm(usfm);
        var verse = VerseFor(date) ?? new DailyVerse { Date = date };
        verse.Usfm = usfm;
        verse.Reference = parsed?.ToString() ?? usfm;
        verse.Source = "youversion";
        verse.Version = S.VersionId.ToString();
        try
        {
            using var t = await c.GetAsync($"v1/bibles/{S.VersionId}/passages/{Uri.EscapeDataString(usfm)}?format=text", ct);
            if (t.IsSuccessStatusCode)
            {
                var pj = JsonDocument.Parse(await t.Content.ReadAsStringAsync(ct)).RootElement;
                verse.Text = pj.TryGetProperty("content", out var content) ? (content.GetString() ?? "").Trim() : "";
                if (pj.TryGetProperty("reference", out var reference) && reference.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(reference.GetString()))
                    verse.Reference = reference.GetString()!;
            }
            else _log.LogInformation("Verse text not available for version {Version}: {Status}", S.VersionId, (int)t.StatusCode);
        }
        catch (Exception ex) { _log.LogInformation(ex, "Verse text fetch failed"); }
        await _verses.UpsertAsync(verse);
        return verse;
    }

    /// <summary>You set the day's verse yourself (no app key, or a different plan).</summary>
    public async Task<DailyVerse> SetVerseAsync(DateOnly date, string reference, string text = "")
    {
        var parsed = BibleBooks.Parse(reference).FirstOrDefault();
        var verse = VerseFor(date) ?? new DailyVerse { Date = date };
        verse.Reference = parsed?.ToString() ?? reference.Trim();
        verse.Usfm = parsed?.Usfm ?? "";
        verse.Text = text;
        verse.Source = "manual";
        await _verses.UpsertAsync(verse);
        return verse;
    }

    public async Task MarkVerseReadAsync(DateOnly date, string how, bool read = true)
    {
        var verse = VerseFor(date);
        if (verse is null)
        {
            if (!read) return;
            verse = new DailyVerse { Date = date };
        }
        if (verse.Read == read) return;
        verse.Read = read;
        verse.ReadUtc = read ? DateTime.UtcNow : null;
        verse.ReadHow = read ? how : "";
        await _verses.UpsertAsync(verse);
        if (read && !string.IsNullOrWhiteSpace(verse.Reference))
            await LogReadingAsync(_clock.Today, verse.Reference, how == "catch-up" ? "catch-up" : "verse");
        if (!read)
        {
            var logged = _readings.Where(r => r.Date == date && r.Reference == verse.Reference && r.Source == "verse");
            await _readings.DeleteManyAsync(logged.Select(l => l.Id));
        }
    }

    // ------------------------------------------------------------------ reading log

    public async Task<BibleReading?> LogReadingAsync(DateOnly date, string reference, string source = "manual", int? minutes = null, string notes = "")
    {
        var refs = BibleBooks.Parse(reference);
        var normalised = refs.Count > 0 ? string.Join("; ", refs.Select(r => r.ToString())) : reference.Trim();
        if (string.IsNullOrWhiteSpace(normalised)) return null;
        if (_readings.Where(r => r.Date == date && r.Reference == normalised && r.Source == source).FirstOrDefault() is { } existing) return existing;
        var reading = new BibleReading { Date = date, Reference = normalised, Source = source, Minutes = minutes, Notes = notes };
        await _readings.UpsertAsync(reading);
        return reading;
    }

    public IReadOnlyList<BibleReading> Recent(int max = 30) =>
        _readings.All().OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedUtc).Take(max).ToList();

    /// <summary>Readings that overlap a passage (for "you've read this before").</summary>
    public IReadOnlyList<BibleReading> ReadingsOf(IEnumerable<BibleRef> refs)
    {
        var list = refs.ToList();
        return _readings.All().Where(r => BibleBooks.Parse(r.Reference).Any(x => list.Any(x.SameChapterAs)))
            .OrderByDescending(r => r.Date).ToList();
    }

    // ------------------------------------------------------------------ app time

    public double AppMinutes(DateOnly date)
    {
        var zone = _clock.Zone;
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone);
        var endUtc = startUtc.AddDays(1);
        var now = DateTime.UtcNow;
        return _sessions.Where(s => s.StartUtc < endUtc && (s.EndUtc ?? now) > startUtc)
            .Sum(s => ((s.EndUtc ?? now) < endUtc ? (s.EndUtc ?? now) : endUtc).Subtract(s.StartUtc > startUtc ? s.StartUtc : startUtc).TotalMinutes);
    }

    /// <summary>The Bible app was opened/closed (iPhone Shortcut, the API, or the HA sensor).</summary>
    public async Task AppEventAsync(bool opened, string source, string device, DateTime? atUtc = null)
    {
        var at = atUtc ?? DateTime.UtcNow;
        var open = _sessions.Where(s => s.EndUtc is null && s.Device == device).OrderByDescending(s => s.StartUtc).FirstOrDefault();
        if (opened)
        {
            if (open is not null) return;   // already counting
            await _sessions.UpsertAsync(new BibleAppSession { StartUtc = at, Source = source, Device = device });
        }
        else if (open is not null)
        {
            // Cap forgotten sessions (app left open on the screen) at an hour.
            open.EndUtc = at - open.StartUtc > TimeSpan.FromHours(1) ? open.StartUtc.AddHours(1) : at;
            await _sessions.UpsertAsync(open);
        }
        await CheckAppReadingAsync();
    }

    /// <summary>Enough time in the app today → today's verse counts as read.</summary>
    public async Task CheckAppReadingAsync()
    {
        var s = S;
        if (!s.AppCountsAsVerse) return;
        var today = _clock.Today;
        if (AppMinutes(today) < Math.Max(0.5, s.MinMinutes)) return;
        var verse = VerseFor(today);
        if (verse is { Read: true }) return;
        await MarkVerseReadAsync(today, "app");
    }

    // ------------------------------------------------------------------ keeping on track

    /// <summary>
    /// Runs every minute (cheap when there's nothing to do): makes sure today's verse exists, moves missed
    /// verses and unfinished readings onto today's list, and adds the reading plan's chapters.
    /// </summary>
    public async Task RollDayAsync(CancellationToken ct = default)
    {
        if (!await _rollLock.WaitAsync(0, ct)) return;
        try
        {
            var s = S;
            var today = _clock.Today;

            if (s.CarryOver)
            {
                // Missed verses from the last week → catch-up readings today.
                foreach (var v in _verses.Where(v => v.Date < today && v.Date >= today.AddDays(-7) && !v.Read && v.CarriedTo is null &&
                                                     !string.IsNullOrWhiteSpace(v.Reference)))
                {
                    await _plan.UpsertAsync(new ReadingPlanItem
                    {
                        Date = today, Reference = v.Reference, Kind = "catch-up", OriginalDate = v.Date, VerseId = v.Id
                    });
                    v.CarriedTo = today;
                    await _verses.UpsertAsync(v);
                }
                // Unfinished readings move forward.
                var stale = _plan.Where(p => !p.Done && p.Date < today);
                foreach (var p in stale)
                {
                    p.OriginalDate ??= p.Date;
                    p.Date = today;
                    p.TimesCarried++;
                }
                if (stale.Count > 0) await _plan.UpsertManyAsync(stale);
            }

            if (s.PlanEnabled && !_plan.Where(p => p.Kind == "plan" && (p.Date == today || !p.Done)).Any())
                await AddPlanChaptersAsync(today, s);

            if (HasYouVersion && VerseFor(today) is null && DateTime.UtcNow - _lastVerseAttemptUtc > TimeSpan.FromMinutes(30))
            {
                _lastVerseAttemptUtc = DateTime.UtcNow;
                try { await FetchVerseAsync(today, ct); }
                catch (Exception ex) { _log.LogInformation("Verse of the day not fetched yet: {Message}", ex.Message); }
            }
        }
        finally { _rollLock.Release(); }
    }

    private async Task AddPlanChaptersAsync(DateOnly today, BibleSettings s)
    {
        var book = BibleBooks.Find(s.PlanBook) ?? BibleBooks.All[42];
        var chapter = Math.Max(1, s.PlanNextChapter);
        var items = new List<ReadingPlanItem>();
        for (var i = 0; i < Math.Clamp(s.PlanChaptersPerDay, 1, 10); i++)
        {
            if (chapter > book.Chapters) { book = BibleBooks.All[(book.Index + 1) % BibleBooks.All.Count]; chapter = 1; }
            items.Add(new ReadingPlanItem { Date = today, Kind = "plan", Reference = new BibleRef(book.Index, chapter).ToString() });
            chapter++;
        }
        if (chapter > book.Chapters) { book = BibleBooks.All[(book.Index + 1) % BibleBooks.All.Count]; chapter = 1; }
        await _plan.UpsertManyAsync(items);
        var (nextBook, nextChapter) = (book.Name, chapter);
        await _settings.UpdateAsync(x => { x.Bible.PlanBook = nextBook; x.Bible.PlanNextChapter = nextChapter; });
    }

    public IReadOnlyList<ReadingPlanItem> TodayList()
    {
        var today = _clock.Today;
        return _plan.Where(p => p.Date == today || (!p.Done && p.Date < today))
            .OrderBy(p => p.Done).ThenBy(p => p.Kind == "catch-up" ? 0 : 1).ThenBy(p => p.CreatedUtc).ToList();
    }

    public async Task CompleteAsync(Guid id, bool done = true)
    {
        var item = _plan.Get(id);
        if (item is null || item.Done == done) return;
        item.Done = done;
        item.DoneOn = done ? _clock.Today : null;
        await _plan.UpsertAsync(item);
        if (item.VerseId is { } vid && _verses.Get(vid) is { } verse)
        {
            verse.Read = done;
            verse.ReadUtc = done ? DateTime.UtcNow : null;
            verse.ReadHow = done ? "catch-up" : "";
            await _verses.UpsertAsync(verse);
        }
        if (done) await LogReadingAsync(_clock.Today, item.Reference, item.Kind == "catch-up" ? "catch-up" : item.Kind);
        else
        {
            var logged = _readings.Where(r => r.Date == _clock.Today && r.Reference == item.Reference && r.Source is "plan" or "catch-up" or "manual");
            await _readings.DeleteManyAsync(logged.Select(l => l.Id));
        }
    }

    public Task AddToListAsync(string reference) =>
        _plan.UpsertAsync(new ReadingPlanItem { Date = _clock.Today, Kind = "manual", Reference = BibleBooks.Normalise(new[] { reference }).FirstOrDefault() ?? reference.Trim() });

    // ------------------------------------------------------------------ stats

    public BibleDay Day(DateOnly date)
    {
        var verse = VerseFor(date);
        return new BibleDay(date, _readings.Where(r => r.Date == date).Count, AppMinutes(date),
            _studies.Where(s => s.Date == date).Count, verse?.Read == true);
    }

    public bool ReadOn(DateOnly date) => Day(date).Level(S.MinMinutes) >= 2;

    /// <summary>Consecutive days with reading (today counts once you've read; otherwise the streak runs to yesterday).</summary>
    public int Streak()
    {
        var day = _clock.Today;
        if (!ReadOn(day)) day = day.AddDays(-1);
        var n = 0;
        while (ReadOn(day) && n < 3650) { n++; day = day.AddDays(-1); }
        return n;
    }

    public IReadOnlyList<BibleDay> LastDays(int days)
    {
        var today = _clock.Today;
        return Enumerable.Range(0, days).Select(i => Day(today.AddDays(i - days + 1))).ToList();
    }

    /// <summary>Chapters read per book (from your reading log and studies).</summary>
    public Dictionary<int, HashSet<int>> Coverage()
    {
        var map = new Dictionary<int, HashSet<int>>();
        void Add(BibleRef r)
        {
            if (!map.TryGetValue(r.Book, out var set)) map[r.Book] = set = new HashSet<int>();
            for (var c = r.Chapter; c <= r.LastChapter; c++) set.Add(c);
        }
        foreach (var r in _readings.All()) foreach (var x in BibleBooks.Parse(r.Reference)) Add(x);
        foreach (var s in _studies.All()) foreach (var p in s.Passages) foreach (var x in BibleBooks.Parse(p)) Add(x);
        return map;
    }
}
