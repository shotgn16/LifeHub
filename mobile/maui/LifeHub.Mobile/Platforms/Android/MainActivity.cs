using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using LifeHub.Mobile.Mobile;

namespace LifeHub.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// Share text or a link from any app to LifeHub.
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        HandleIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }

    private static void HandleIntent(Intent? intent)
    {
        if (intent is null) return;
        if (intent.Action == Intent.ActionSend && intent.GetStringExtra(Intent.ExtraText) is { Length: > 0 } text)
            PendingNavigation.Request("share?text=" + Uri.EscapeDataString(text));
        else if (intent.GetStringExtra("link") is { Length: > 0 } link)
            PendingNavigation.Request(link);
    }
}
