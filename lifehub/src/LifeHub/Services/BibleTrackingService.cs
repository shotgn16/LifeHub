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
    private bool _backfilled;
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

    private bool IsBibleApp(string state, string match) =>
        !string.IsNullOrWhiteSpace(state) &&
        (state.Contains(match, StringComparison.OrdinalIgnoreCase) ||
         (match.Length == 0 && (state.Contains("bible", StringComparison.OrdinalIgnoreCase) || state.Contains("youversion", StringComparison.OrdinalIgnoreCase))));

    public async Task WatchAppAsync()
    {
        var ha = S<HomeAssistantClient>();
        var s = S<SettingsService>().Current.Bible;
        if (!ha.IsConfigured || s.AppSensors.Count == 0) return;
        var bible = S<BibleService>();
        var match = s.AppMatch.Trim();

        if (!_backfilled)
        {
            _backfilled = true;
            foreach (var sensor in s.AppSensors) await BackfillAsync(ha, bible, sensor, match);
        }

        foreach (var sensor in s.AppSensors)
        {
            var st = await ha.GetStateAsync(sensor);
            if (st is null) continue;
            var active = IsBibleApp(st.State, match);
            var changed = st.LastChanged == default ? DateTime.UtcNow : st.LastChanged.ToUniversalTime();
            var open = bible.Sessions.Where(x => x.EndUtc is null && x.Device == sensor).Any();
            if (active && !open) await bible.AppEventAsync(true, "ha", sensor, changed);
            else if (!active && open) await bible.AppEventAsync(false, "ha", sensor, changed);
        }
        LastStatus = $"Checked {s.AppSensors.Count} phone(s) at {DateTime.Now:HH:mm}";
        await bible.CheckAppReadingAsync();
    }

    /// <summary>After a restart: rebuild the last day's Bible-app sessions from HA history.</summary>
    private async Task BackfillAsync(HomeAssistantClient ha, BibleService bible, string sensor, string match)
    {
        try
        {
            var since = DateTime.UtcNow.AddHours(-24);
            var changes = await ha.GetStateHistoryAsync(sensor, since);
            DateTime? start = null;
            foreach (var (utc, state) in changes.OrderBy(c => c.Utc))
            {
                var active = IsBibleApp(state, match);
                if (active && start is null) start = utc;
                else if (!active && start is { } st)
                {
                    var overlap = bible.Sessions.Where(x => x.Device == sensor && x.StartUtc < utc && (x.EndUtc ?? DateTime.UtcNow) > st).Any();
                    if (!overlap)
                        await bible.Sessions.UpsertAsync(new BibleAppSession
                        {
                            StartUtc = st, EndUtc = utc - st > TimeSpan.FromHours(1) ? st.AddHours(1) : utc, Source = "ha", Device = sensor
                        });
                    start = null;
                }
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "Bible app history backfill failed for {Sensor}", sensor); }
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
