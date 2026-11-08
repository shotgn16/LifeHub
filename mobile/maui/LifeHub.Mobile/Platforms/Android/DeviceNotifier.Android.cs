using Android.App;
using Android.Content;

namespace LifeHub.Mobile.Mobile;

public sealed partial class DeviceNotifier
{
    private const string Group = "lifehub";
    private static bool _channels;

    private static void EnsureChannels(NotificationManager nm)
    {
        if (_channels || !OperatingSystem.IsAndroidVersionAtLeast(26)) return;
        foreach (var c in NoticeCategories.All)
        {
            var importance = c.Key is NoticeCategories.Reminders or NoticeCategories.Renewals ? NotificationImportance.High : NotificationImportance.Default;
            nm.CreateNotificationChannel(new NotificationChannel(c.Key, c.Title, importance) { Description = c.Help });
        }
        _channels = true;
    }

    private partial void PlatformShow(string title, string message, string category, string link, string tag)
    {
        var ctx = Platform.AppContext;
        var nm = (NotificationManager)ctx.GetSystemService(Context.NotificationService)!;
        EnsureChannels(nm);

        var open = new Intent(ctx, typeof(MainActivity)).SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop).PutExtra("link", link);
        var pending = PendingIntent.GetActivity(ctx, (tag + link).GetHashCode(), open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var id = string.IsNullOrEmpty(tag) ? Environment.TickCount : tag.GetHashCode();

        Notification.Builder Builder() => OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(ctx, category) : new Notification.Builder(ctx);

        var n = Builder()
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(unchecked((int)0xFF0F766E))
            .SetContentTitle(title)
            .SetContentText(message.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.TrimStart('•', ' ') ?? "")
            .SetStyle(new Notification.BigTextStyle().BigText(message))
            .SetContentIntent(pending)
            .SetAutoCancel(true)
            .SetGroup(Group)
            .Build();
        nm.Notify(category, id, n);

        // One summary keeps several LifeHub notifications together.
        var summary = Builder()
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(unchecked((int)0xFF0F766E))
            .SetContentTitle("LifeHub")
            .SetGroup(Group)
            .SetGroupSummary(true)
            .SetAutoCancel(true)
            .Build();
        nm.Notify("summary", 0, summary);
    }
}
