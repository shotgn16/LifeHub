using LifeHub.Integrations.HomeAssistant;

namespace LifeHub.Mobile.Mobile;

/// <summary>
/// What the phone does by itself.
/// <list type="bullet">
/// <item>While the app is open (on its own): LifeHub's usual background jobs — email every few minutes, reminders,
/// Home Assistant lists — exactly as on a server (Settings → Background tasks).</item>
/// <item>When the app is opened: check email straight away (and, connected to a server, fetch its notifications).</item>
/// <item>When the app is closed: a background check every 15/30/60 minutes on Android (JobScheduler), and whenever
/// iOS allows on iPhone and iPad (Background App Refresh). Settings → This phone.</item>
/// </list>
/// </summary>
public sealed partial class PhoneJobs
{
    private readonly IServiceProvider _sp;
    private readonly MobileState _state;
    private bool _started;

    public PhoneJobs(IServiceProvider sp, MobileState state)
    {
        _sp = sp; _state = state;
    }

    public string LastBackgroundRun
    {
        get => Preferences.Default.Get("lastBackground", "");
        private set => Preferences.Default.Set("lastBackground", value);
    }

    public void AppOpened()
    {
        Schedule(_state);
        if (_state.Mode == MobileMode.Device)
        {
            StartInAppJobs();
            if (_state.CheckOnOpen) _sp.GetRequiredService<EmailSyncService>().SyncSoon("opened");
        }
        else if (_state.Mode == MobileMode.Server && _state.CheckOnOpen)
            _ = Task.Run(() => _sp.GetRequiredService<ServerClient>().CheckAsync(openedApp: true));
    }

    private void StartInAppJobs()
    {
        if (_started) return;
        _started = true;
        foreach (var job in new IHostedService[]
                 {
                     _sp.GetRequiredService<EmailSyncService>(), _sp.GetRequiredService<AutomationService>(), _sp.GetRequiredService<HaListSyncService>()
                 })
            _ = job.StartAsync(CancellationToken.None);
    }

    /// <summary>The background check (Android job / iOS refresh): runs once, quickly, then finishes.</summary>
    public async Task RunBackgroundAsync(CancellationToken ct)
    {
        try
        {
            MauiProgram.UseThePhonesPerson(_sp);
            if (_state.Mode == MobileMode.Device)
            {
                using var system = UserScope.AsSystem();
                var summary = _sp.GetRequiredService<SettingsService>().Current.Background.IsOn(BackgroundJobs.Email)
                    ? await _sp.GetRequiredService<EmailSyncService>().SyncAllAsync(ct) : "email check off";
                await _sp.GetRequiredService<AutomationService>().RunOnceAsync(ct);
                LastBackgroundRun = $"{DateTime.Now:ddd HH:mm} — {summary}";
            }
            else if (_state.Mode == MobileMode.Server)
            {
                var r = await _sp.GetRequiredService<ServerClient>().CheckAsync(openedApp: false, ct);
                LastBackgroundRun = $"{DateTime.Now:ddd HH:mm} — {r}";
            }
        }
        catch (Exception ex) { LastBackgroundRun = $"{DateTime.Now:ddd HH:mm} — failed: {ex.Message}"; }
    }

    /// <summary>(Re)schedules the background check to match Settings → This phone.</summary>
    public static void Schedule(MobileState state) => PlatformSchedule(state.Mode == MobileMode.None ? 0 : state.BackgroundMinutes);

    static partial void PlatformScheduleImpl(int minutes);
    private static void PlatformSchedule(int minutes) => PlatformScheduleImpl(minutes);
}
