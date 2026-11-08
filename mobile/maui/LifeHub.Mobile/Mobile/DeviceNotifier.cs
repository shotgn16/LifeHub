namespace LifeHub.Mobile.Mobile;

/// <summary>
/// Shows LifeHub's notifications on the phone, tidily: one Android channel (or iOS thread) per kind — reminders,
/// renewals, food, email… — so each can be silenced in the phone's own settings, grouped under one "LifeHub" summary.
/// </summary>
public sealed partial class DeviceNotifier : IDeviceNotifier
{
    public Task ShowAsync(Notice notice, string? tag) => Show(notice.Title, notice.Message, notice.Category, notice.Link, tag ?? notice.DedupeKey);

    /// <summary>For notifications fetched from a LifeHub server (server mode).</summary>
    public Task Show(string title, string message, string category, string link, string? tag)
    {
        try { PlatformShow(title, message, string.IsNullOrEmpty(category) ? NoticeCategories.System : category, link ?? "", tag ?? ""); }
        catch { }
        return Task.CompletedTask;
    }

    public static async Task<bool> RequestPermissionAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
        return true;
#elif IOS
        var (granted, _) = await UserNotifications.UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UserNotifications.UNAuthorizationOptions.Alert | UserNotifications.UNAuthorizationOptions.Badge | UserNotifications.UNAuthorizationOptions.Sound);
        return granted;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    public static async Task<bool> AllowedAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted;
        return true;
#elif IOS
        var s = await UserNotifications.UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return s.AuthorizationStatus is UserNotifications.UNAuthorizationStatus.Authorized or UserNotifications.UNAuthorizationStatus.Provisional;
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    private partial void PlatformShow(string title, string message, string category, string link, string tag);
}
