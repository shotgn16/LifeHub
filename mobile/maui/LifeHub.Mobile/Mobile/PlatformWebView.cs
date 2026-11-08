using Microsoft.AspNetCore.Components.WebView;

namespace LifeHub.Mobile.Mobile;

/// <summary>Web view tweaks: the camera for the barcode scanner, inline video, and no zooming.</summary>
public static partial class PlatformWebView
{
    public static void Initializing(object? sender, BlazorWebViewInitializingEventArgs e)
    {
#if IOS
        e.Configuration.AllowsInlineMediaPlayback = true;
        e.Configuration.MediaTypesRequiringUserActionForPlayback = WebKit.WKAudiovisualMediaTypes.None;
#endif
    }

    public static void Initialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
#if ANDROID
        CameraChromeClient.Attach(e.WebView);
        e.WebView.Settings.MediaPlaybackRequiresUserGesture = false;
#endif
    }
}
