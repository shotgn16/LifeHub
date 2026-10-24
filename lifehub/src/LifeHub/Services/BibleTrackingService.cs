using LifeHub.Integrations.HomeAssistant;

namespace LifeHub.Services;

/// <summary>
/// Every minute: keeps the Bible reading list rolling (carry-over, plan, verse of the day), watches the
/// phone's "last used app" sensor in Home Assistant to time Bible-app sessions, sends the evening
/// reminder and publishes sensor.lifehub_bible_* to HA.
/// </summary>
public sealed class BibleTrackingService : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<BibleTrackingService> _log;
    private DateTime _lastSensorsUtc = DateTime.MinValue;

    public string LastStatus { get; private set; } = "";

    public BibleTrackingService(IServiceProvider sp, ILogger<BibleTrackingService> log) { _sp = sp; _log = log; }

    private T S<T>() where T : notnull => _sp.GetRequiredService<T>();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(12), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            if (!S<SettingsService>().Current.Bible.Enabled) continue;
            await Step(() => S<BibleService>().RollDayAsync(ct), "roll");
            await Step(WatchAppAsync, "app");
            await Step(ReminderAsync, "reminder");
            await Step(PublishAsync, "sensors");
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task Step(Func<Task> f, string name)
    {
        try { await f(); }
        catch (Exception ex) { _log.LogDebug(ex, "Bible step {Step} failed", name); LastStatus = $"{name}: {ex.Message}"; }
    }

    /// <summary>Phones whose screen on/off sensor was found (for the settings page).</summary>
    public Dictionary<string, string?> ScreenSensors { get; } = new();
    private DateTime _screenLookupUtc = DateTime.MinValue;

    /// <summary>
    /// Rebuilds today's (and yesterday evening's) sessions from Home Assistant history every minute, using the
    /// phone's "Last used app" sensor together with its screen on/off ("Interactive") sensor when available.
    /// Rebuilding from history (rather than sampling the current state) catches short sessions between checks
    /// and corrects itself if HA was briefly unreachable.
    /// </summary>
    public async Task WatchAppAsync()
    {
        var ha = S<HomeAssistantClient>();
        var s = S<SettingsService>().Current.Bible;
        var bible = S<BibleService>();
        if (ha.IsConfigured && s.AppSensors.Count > 0)
        {
            if (DateTime.UtcNow - _screenLookupUtc > TimeSpan.FromMinutes(10))
            {
                _screenLookupUtc = DateTime.UtcNow;
                foreach (var sensor in s.AppSensors)
                {
                    var screen = BibleSessions.ScreenSensorFor(sensor);
                    ScreenSensors[sensor] = screen is not null && await ha.GetStateAsync(screen) is not null ? screen : null;
                }
            }
            var now = DateTime.UtcNow;
            var (dayStart, _) = bible.DayUtc(S<Clock>().Today);
            var from = dayStart.AddHours(-6);                 // include late-evening reading from the night before
            foreach (var sensor in s.AppSensors)
            {
                var app = await ha.GetStateHistoryAsync(sensor, from.AddHours(-2));
                var screenId = ScreenSensors.GetValueOrDefault(sensor);
                var screen = screenId is null ? null : await ha.GetStateHistoryAsync(screenId, from.AddHours(-2));
                var sessions = BibleSessions.Build(app, screen, now, s.AppMatch, Math.Max(0, s.MinSessionSeconds));
                await bible.ReplaceSessionsAsync(sensor, from, sessions);
            }
            LastStatus = $"Checked {s.AppSensors.Count} phone(s) at {S<Clock>().Now:HH:mm}";
        }
        await bible.CheckAppReadingAsync();
        if (s.AskWhatIRead) await AskWhatIReadAsync(bible, s);
    }

    /// <summary>After a real reading session (and nothing logged since it started), ask what was read — once per session.</summary>
    private async Task AskWhatIReadAsync(BibleService bible, BibleSettings s)
    {
        var now = DateTime.UtcNow;
        var ended = bible.Sessions.Where(x => !x.Ignored && !x.Asked && x.EndUtc is { } e &&
                                              now - e >= TimeSpan.FromMinutes(2) && now - e <= TimeSpan.FromHours(3) &&
                                              (e - x.StartUtc).TotalMinutes >= Math.Max(1, s.MinMinutes));
        foreach (var session in ended.OrderBy(x => x.StartUtc))
        {
            session.Asked = true;
            await bible.Sessions.UpsertAsync(session);
            // Already told LifeHub what you read (shared it, ticked the plan, logged it)? No need to ask.
            var logged = bible.Readings.Where(r => r.CreatedUtc >= session.StartUtc.AddMinutes(-5) && r.Source is not "verse");
            if (logged.Count > 0) continue;
            var minutes = (int)Math.Round((session.EndUtc!.Value - session.StartUtc).TotalMinutes);
            var picks = bible.QuickPicks().Take(3).Select(p => p.Reference).ToList();
            var body = picks.Count > 0 ? "Tap to log it — e.g. " + string.Join(", ", picks) : "Tap to log what you read.";
            await S<NotificationService>().NotifyAsync($"📖 {minutes} min in the Bible app — what did you read?", body,
                NoticeLevel.Info, "bible/log", $"bible-ask:{session.Id}", tag: "bible-ask");
        }
    }

    private async Task ReminderAsync()
    {
        var s = S<SettingsService>().Current.Bible;
        if (!s.Reminder) return;
        var clock = S<Clock>();
        if (TimeOnly.FromDateTime(clock.Now) < s.ReminderTime) return;
        var bible = S<BibleService>();
        var today = clock.Today;
        if (bible.ReadOn(today)) return;
        var verse = bible.VerseFor(today);
        var list = bible.TodayList().Where(p => !p.Done).ToList();
        var lines = new List<string>();
        if (verse is { Read: false } && !string.IsNullOrWhiteSpace(verse.Reference)) lines.Add($"Verse of the day · {verse.Reference}");
        lines.AddRange(list.Select(p => p.Kind == "catch-up" ? $"Catch up · {p.Reference}" : p.Reference));
        var streak = bible.Streak();
        var title = streak > 0 ? $"📖 Keep your {streak}-day streak going" : "📖 Time for today's reading";
        var body = lines.Count > 0 ? NotificationService.Bullets(lines, 4) : "A few minutes in the Bible app counts.";
        if (verse is { Text.Length: > 0 } && verse.Text.Length < 160) body = $"“{verse.Text}”\n{body}";
        await S<NotificationService>().NotifyAsync(title, body, NoticeLevel.Info, "bible", $"bible-remind:{today}", tag: "bible");
    }

    private async Task PublishAsync()
    {
        if (DateTime.UtcNow - _lastSensorsUtc < TimeSpan.FromMinutes(5)) return;
        var ha = S<HomeAssistantClient>();
        if (!ha.IsConfigured || !S<SettingsService>().Current.HomeAssistant.PublishSensors) return;
        _lastSensorsUtc = DateTime.UtcNow;
        var bible = S<BibleService>();
        var today = S<Clock>().Today;
        var verse = bible.VerseFor(today);
        await ha.SetStateAsync("sensor.lifehub_bible_streak", bible.Streak().ToString(), new()
        {
            ["friendly_name"] = "LifeHub Bible streak", ["icon"] = "mdi:book-open-variant", ["unit_of_measurement"] = "days",
            ["read_today"] = bible.ReadOn(today),
            ["verse"] = verse?.Reference, ["verse_read"] = verse?.Read == true,
            ["app_minutes_today"] = Math.Round(bible.AppMinutes(today), 1),
            ["to_read"] = bible.TodayList().Where(p => !p.Done).Select(p => p.Reference).ToArray()
        });
    }
}
