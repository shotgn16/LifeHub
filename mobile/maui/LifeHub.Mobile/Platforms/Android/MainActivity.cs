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
        KeepClearOfSystemBars();
        HandleIntent(Intent);
    }

    /// <summary>
    /// Android 15 draws apps edge to edge, under the status bar, the camera cut-out and the gesture bar. LifeHub's pages
    /// aren't made for that (buttons ended up behind the camera), so the app's content is padded to sit between them,
    /// and above the keyboard when it's open.
    /// </summary>
    private void KeepClearOfSystemBars()
    {
        if (FindViewById(Android.Resource.Id.Content) is not { } content) return;
        AndroidX.Core.View.ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarPadding());
    }

    private sealed class SystemBarPadding : Java.Lang.Object, AndroidX.Core.View.IOnApplyWindowInsetsListener
    {
        public AndroidX.Core.View.WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? v, AndroidX.Core.View.WindowInsetsCompat? insets)
        {
            if (v is null || insets is null) return insets!;
            var types = AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars() | AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout() |
                        AndroidX.Core.View.WindowInsetsCompat.Type.Ime();
            var bars = insets.GetInsets(types);
            v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            return AndroidX.Core.View.WindowInsetsCompat.Consumed!;
        }
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
