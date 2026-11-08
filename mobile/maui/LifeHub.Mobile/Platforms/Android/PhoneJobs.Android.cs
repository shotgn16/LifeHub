using Android.App;
using Android.App.Job;
using Android.Content;

namespace LifeHub.Mobile.Mobile;

public sealed partial class PhoneJobs
{
    private const int JobId = 7001;

    static partial void PlatformScheduleImpl(int minutes)
    {
        var ctx = Platform.AppContext;
        var scheduler = (JobScheduler)ctx.GetSystemService(Context.JobSchedulerService)!;
        if (minutes <= 0) { scheduler.Cancel(JobId); return; }
        var existing = scheduler.GetPendingJob(JobId);
        var interval = (long)TimeSpan.FromMinutes(Math.Max(15, minutes)).TotalMilliseconds;
        if (existing is not null && existing.IntervalMillis == interval) return;
        var job = new JobInfo.Builder(JobId, new ComponentName(ctx, Java.Lang.Class.FromType(typeof(LifeHubJobService))))
            .SetPeriodic(interval)
            .SetRequiredNetworkType(NetworkType.Any)
            .SetPersisted(true)
            .Build();
        scheduler.Schedule(job);
    }
}

/// <summary>Android's background check (every 15 minutes or more, when Android allows; survives restarts).</summary>
[Service(Name = "io.github.shotgn16.lifehub.LifeHubJobService", Permission = "android.permission.BIND_JOB_SERVICE", Exported = true)]
public sealed class LifeHubJobService : JobService
{
    private CancellationTokenSource? _cts;

    public override bool OnStartJob(JobParameters? args)
    {
        _cts = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var cts = _cts;
        _ = Task.Run(async () =>
        {
            try
            {
                var jobs = IPlatformApplication.Current?.Services.GetService<PhoneJobs>();
                if (jobs is not null) await jobs.RunBackgroundAsync(cts.Token);
            }
            finally { JobFinished(args, false); }
        });
        return true;   // still working
    }

    public override bool OnStopJob(JobParameters? args)
    {
        _cts?.Cancel();
        return true;   // try again later
    }
}
