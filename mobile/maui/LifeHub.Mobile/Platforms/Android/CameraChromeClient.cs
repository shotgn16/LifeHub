using Android.Webkit;
using AWebView = Android.Webkit.WebView;

namespace LifeHub.Mobile.Mobile;

/// <summary>
/// Lets pages in the app use the camera (barcode scanner, photos): asks Android for camera permission, then grants it
/// to the page. Everything else (file pickers for "Add from photo" and uploads) is passed to the web view's own client.
/// </summary>
public sealed class CameraChromeClient : WebChromeClient
{
    private readonly WebChromeClient? _inner;
    private CameraChromeClient(WebChromeClient? inner) => _inner = inner;

    public static void Attach(AWebView view)
    {
        WebChromeClient? inner = OperatingSystem.IsAndroidVersionAtLeast(26) ? view.WebChromeClient : null;
        view.SetWebChromeClient(new CameraChromeClient(inner));
    }

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request is null) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var wantsCamera = request.GetResources()?.Contains(PermissionRequest.ResourceVideoCapture) == true;
            var ok = !wantsCamera || await Permissions.RequestAsync<Permissions.Camera>() == PermissionStatus.Granted;
            if (ok) request.Grant(request.GetResources()); else request.Deny();
        });
    }

    public override bool OnShowFileChooser(AWebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams) =>
        _inner?.OnShowFileChooser(webView, filePathCallback, fileChooserParams) ?? base.OnShowFileChooser(webView, filePathCallback, fileChooserParams);

    public override bool OnCreateWindow(AWebView? view, bool isDialog, bool isUserGesture, Android.OS.Message? resultMsg) =>
        _inner?.OnCreateWindow(view, isDialog, isUserGesture, resultMsg) ?? base.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
}
