using Foundation;
using UserNotifications;

namespace LifeHub.Mobile.Mobile;

public sealed partial class DeviceNotifier
{
    private partial void PlatformShow(string title, string message, string category, string link, string tag)
    {
        var content = new UNMutableNotificationContent
        {
            Title = title,
            Body = message,
            ThreadIdentifier = category,          // iOS groups notifications of the same kind
            Sound = UNNotificationSound.Default,
            UserInfo = NSDictionary.FromObjectAndKey(new NSString(link), new NSString("link"))
        };
        var id = string.IsNullOrEmpty(tag) ? Guid.NewGuid().ToString() : category + ":" + tag;
        UNUserNotificationCenter.Current.AddNotificationRequest(UNNotificationRequest.FromIdentifier(id, content, null), _ => { });
    }
}
