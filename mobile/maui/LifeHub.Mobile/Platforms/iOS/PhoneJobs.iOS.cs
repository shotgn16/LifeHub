using BackgroundTasks;
using Foundation;

namespace LifeHub.Mobile.Mobile;

public sealed partial class PhoneJobs
{
    public const string RefreshTask = "io.github.shotgn16.lifehub.refresh";
    private static int _minutes = 15;

    static partial void PlatformScheduleImpl(int minutes)
    {
        _minutes = minutes;
        BGTaskScheduler.Shared.Cancel(RefreshTask);
        if (minutes <= 0) return;
        // iOS decides when it actually runs (it learns when you use the app); this is the earliest.
        var request = new BGAppRefreshTaskRequest(RefreshTask) { EarliestBeginDate = NSDate.FromTimeIntervalSinceNow(minutes * 60) };
        BGTaskScheduler.Shared.Submit(request, out _);
    }

    /// <summary>Registered once at launch (AppDelegate).</summary>
    public static void Register() =>
        BGTaskScheduler.Shared.Register(RefreshTask, null, task =>
        {
            var cts = new CancellationTokenSource();
            task.ExpirationHandler = () => cts.Cancel();
            PlatformScheduleImpl(_minutes);    // next time
            _ = Task.Run(async () =>
            {
                var ok = true;
                try
                {
                    var jobs = IPlatformApplication.Current?.Services.GetService<PhoneJobs>();
                    if (jobs is not null) await jobs.RunBackgroundAsync(cts.Token);
                }
                catch { ok = false; }
                task.SetTaskCompleted(ok && !cts.IsCancellationRequested);
            });
        });
}
