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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DateTime> _lastVerseAttemptUtc = new();   // per person

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

    /// <summary>The text of a passage in your chosen version (empty without a YouVersion key, or if the version isn't licensed).</summary>
    public async Task<string> PassageTextAsync(BibleRef r, CancellationToken ct = default)
    {
        if (!HasYouVersion) return "";
        try
        {
            using var c = YouVersion();
            using var t = await c.GetAsync($"v1/bibles/{S.VersionId}/passages/{Uri.EscapeDataString(r.Usfm)}?format=text", ct);
            if (!t.IsSuccessStatusCode) return "";
            var pj = JsonDocument.Parse(await t.Content.ReadAsStringAsync(ct)).RootElement;
            return pj.TryGetProperty("content", out var content) ? (content.GetString() ?? "").Trim() : "";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogInformation("Passage text fetch failed: {Message}", ex.Message);
            return "";
        }
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

    public (DateTime From, DateTime To) DayUtc(DateOnly date)
    {
        var from = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), _clock.Zone);
        return (from, from.AddDays(1));
    }

    public IReadOnlyList<BibleAppSession> SessionsOn(DateOnly date)
    {
        var (from, to) = DayUtc(date);
        var now = DateTime.UtcNow;
        return _sessions.Where(s => s.StartUtc < to && (s.EndUtc ?? now) > from).OrderBy(s => s.StartUtc).ToList();
    }

    public double AppMinutes(DateOnly date)
    {
        var (startUtc, endUtc) = DayUtc(date);
        var now = DateTime.UtcNow;
        return SessionsOn(date).Where(s => !s.Ignored)
            .Sum(s => ((s.EndUtc ?? now) < endUtc ? (s.EndUtc ?? now) : endUtc).Subtract(s.StartUtc > startUtc ? s.StartUtc : startUtc).TotalMinutes);
    }

    /// <summary>The Bible app was opened/closed (iPhone Shortcut or the API).</summary>
    public async Task AppEventAsync(bool opened, string source, string device, DateTime? atUtc = null)
    {
        var at = atUtc ?? DateTime.UtcNow;
        var open = _sessions.Where(s => s.EndUtc is null && s.Device == device).OrderByDescending(s => s.StartUtc).FirstOrDefault();
        if (opened)
        {
            if (open is not null)
            {
                // A missed "closed" (phone died, automation didn't run): end the old one at its cap.
                if (at - open.StartUtc < TimeSpan.FromMinutes(45)) return;
                open.EndUtc = open.StartUtc.AddMinutes(45);
                await _sessions.UpsertAsync(open);
            }
            await _sessions.UpsertAsync(new BibleAppSession { StartUtc = at, Source = source, Device = device });
        }
        else if (open is not null)
        {
            open.EndUtc = at - open.StartUtc > TimeSpan.FromMinutes(45) ? open.StartUtc.AddMinutes(45) : at;
            if ((open.EndUtc.Value - open.StartUtc).TotalSeconds < Math.Max(0, S.MinSessionSeconds)) await _sessions.DeleteAsync(open.Id);
            else await _sessions.UpsertAsync(open);
        }
        await CheckAppReadingAsync();
    }

    /// <summary>
    /// Replaces a phone's sessions in a time window with ones rebuilt from Home Assistant history.
    /// Keeps "not reading" marks and "asked" flags. Returns true if anything changed.
    /// </summary>
    public async Task<bool> ReplaceSessionsAsync(string device, DateTime fromUtc, IReadOnlyList<AppInterval> fresh)
    {
        var existing = _sessions.Where(s => s.Device == device && s.Source == "ha" && s.StartUtc >= fromUtc).ToList();
        static bool Same(DateTime a, DateTime b) => Math.Abs((a - b).TotalSeconds) < 90;
        var desired = fresh.Where(f => f.StartUtc >= fromUtc).Select(f =>
        {
            var match = existing.FirstOrDefault(e => Same(e.StartUtc, f.StartUtc));
            return new BibleAppSession
            {
                Id = match?.Id ?? Guid.NewGuid(), CreatedUtc = match?.CreatedUtc ?? DateTime.UtcNow,
                StartUtc = f.StartUtc, EndUtc = f.EndUtc, Source = "ha", Device = device,
                Ignored = match?.Ignored ?? false, Asked = match?.Asked ?? false
            };
        }).ToList();
        var unchanged = desired.Count == existing.Count && desired.All(d =>
            existing.FirstOrDefault(e => e.Id == d.Id) is { } e && e.StartUtc == d.StartUtc && e.EndUtc == d.EndUtc);
        if (unchanged) return false;
        var remove = existing.Where(e => desired.All(d => d.Id != e.Id)).Select(e => e.Id).ToList();
        if (remove.Count > 0) await _sessions.DeleteManyAsync(remove);
        if (desired.Count > 0) await _sessions.UpsertManyAsync(desired);
        return true;
    }

    public async Task SetIgnoredAsync(Guid sessionId, bool ignored)
    {
        if (_sessions.Get(sessionId) is not { } s) return;
        s.Ignored = ignored;
        await _sessions.UpsertAsync(s);
        // Ticked only because of app time? Untick if the remaining time no longer counts.
        var today = _clock.Today;
        if (ignored && VerseFor(today) is { Read: true, ReadHow: "app" } && AppMinutes(today) < Math.Max(0.5, S.MinMinutes))
            await MarkVerseReadAsync(today, "app", false);
        else await CheckAppReadingAsync();
    }

    // ------------------------------------------------------------------ what did you read?

    public sealed record QuickPick(string Reference, string Why, Guid? PlanItemId = null, bool IsVerse = false);

    /// <summary>One-tap choices for "What did you read?": today's verse and list, then carrying on from where you were.</summary>
    public IReadOnlyList<QuickPick> QuickPicks()
    {
        var today = _clock.Today;
        var picks = new List<QuickPick>();
        if (VerseFor(today) is { Read: false } v && !string.IsNullOrWhiteSpace(v.Reference))
            picks.Add(new QuickPick(v.Reference, "Verse of the day", IsVerse: true));
        foreach (var p in TodayList().Where(p => !p.Done))
            picks.Add(new QuickPick(p.Reference, p.Kind == "catch-up" ? "Catch up" : p.Kind == "plan" ? "Reading plan" : "On today's list", p.Id));
        // Carry on from the last chapter read in each recent book.
        var recentBooks = _readings.All().OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedUtc)
            .SelectMany(r => BibleBooks.Parse(r.Reference).Select(x => (r.Date, Ref: x)))
            .Where(x => x.Ref.Verse is null || x.Ref.EndVerse is not null || x.Ref.EndChapter is not null)   // chapter-ish readings, not single verses
            .GroupBy(x => x.Ref.Book).Take(3);
        foreach (var g in recentBooks)
        {
            var last = g.First().Ref;
            var book = last.BookInfo;
            var next = last.LastChapter + (last.EndVerse is null || last.Verse is null ? 1 : 0);
            if (next > book.Chapters) continue;
            var reference = new BibleRef(book.Index, next).ToString();
            var why = next == last.LastChapter ? $"Rest of the chapter (you read {last})" : $"Carry on from {last}";
            if (picks.All(p => p.Reference != reference)) picks.Add(new QuickPick(reference, why));
        }
        return picks.Take(8).ToList();
    }

    /// <summary>Logs a quick pick (and ticks the plan item / verse it came from).</summary>
    public async Task LogPickAsync(QuickPick pick)
    {
        if (pick.IsVerse) await MarkVerseReadAsync(_clock.Today, "manual");
        else if (pick.PlanItemId is { } id) await CompleteAsync(id);
        else await LogReadingAsync(_clock.Today, pick.Reference, "manual");
    }

    /// <summary>
    /// Something shared from YouVersion (link or text). Logs the passage(s) and ticks matching items on
    /// today's list (and today's verse). Returns what was logged.
    /// </summary>
    public async Task<List<BibleReading>> LogSharedAsync(string shared, string source = "share")
    {
        var refs = BibleBooks.FromShared(shared);
        var today = _clock.Today;
        var logged = new List<BibleReading>();
        foreach (var r in refs)
        {
            if (await LogReadingAsync(today, r.ToString(), source) is { } reading) logged.Add(reading);
            if (VerseFor(today) is { Read: false } v && BibleBooks.Parse(v.Reference).Any(x => x.Overlaps(r)))
                await MarkVerseReadAsync(today, "manual");
            foreach (var p in TodayList().Where(p => !p.Done && BibleBooks.Parse(p.Reference).Any(x => x.Overlaps(r))))
            {
                p.Done = true; p.DoneOn = today;
                await _plan.UpsertAsync(p);
                if (p.VerseId is { } vid && _verses.Get(vid) is { } missed) { missed.Read = true; missed.ReadHow = "catch-up"; await _verses.UpsertAsync(missed); }
            }
        }
        return logged;
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

            if (HasYouVersion && VerseFor(today) is null && DateTime.UtcNow - _lastVerseAttemptUtc.GetValueOrDefault(UserScope.UserId ?? Guid.Empty) > TimeSpan.FromMinutes(30))
            {
                _lastVerseAttemptUtc[UserScope.UserId ?? Guid.Empty] = DateTime.UtcNow;
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
