using Foundation;
using LifeHub.Mobile.Mobile;
using UIKit;
using UserNotifications;

namespace LifeHub.Mobile;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        PhoneJobs.Register();                                      // background refresh (must be before launch finishes)
        UNUserNotificationCenter.Current.Delegate = new NotificationDelegate();
        return base.FinishedLaunching(application, launchOptions);
    }

    /// <summary>Shows notifications while the app is open, and opens the right page when one is tapped.</summary>
    private sealed class NotificationDelegate : UNUserNotificationCenterDelegate
    {
        public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler) =>
            completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List | UNNotificationPresentationOptions.Sound);

        public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
        {
            if (response.Notification.Request.Content.UserInfo?["link"] is NSString link) PendingNavigation.Request(link.ToString());
            completionHandler();
        }
    }
}
