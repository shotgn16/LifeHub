using System.Net;
using System.Text.Json;

namespace LifeHub.Mobile.Mobile;

/// <summary>
/// Connected to a LifeHub server: shows it full screen, signed in with your API key. When it can't be reached the
/// offline screen (offline.html) shows the saved copy and lets you add and tick things; they're sent later.
/// </summary>
public sealed class ServerPage : ContentPage
{
    private readonly IServiceProvider _sp;
    private readonly MobileState _state;
    private readonly ServerClient _client;
    private readonly WebView _web = new();
    private bool _offline;

    public ServerPage(IServiceProvider sp)
    {
        _sp = sp;
        _state = sp.GetRequiredService<MobileState>();
        _client = sp.GetRequiredService<ServerClient>();
        _web.Navigating += OnNavigating;
#if ANDROID
        _web.HandlerChanged += (_, _) =>
        {
            if (_web.Handler?.PlatformView is Android.Webkit.WebView w)
            {
                w.Settings.JavaScriptEnabled = true;
                w.Settings.DomStorageEnabled = true;
                w.Settings.MediaPlaybackRequiresUserGesture = false;
                CameraChromeClient.Attach(w);
            }
        };
#endif
        Content = _web;
        _ = OpenAsync();
    }

    private string Base => _state.ServerUrl.TrimEnd('/');

    public async Task OpenAsync()
    {
        if (await _client.ReachableAsync()) { _offline = false; SignIn(); }
        else await ShowOfflineAsync();
    }

    /// <summary>Signs in by posting the API key to /auth/app (the server sets its cookie and opens the start page).</summary>
    private void SignIn(string returnUrl = "/")
    {
        var html = $"""
            <!doctype html><html><body onload="document.forms[0].submit()" style="background:#121514">
            <form method="post" action="{WebUtility.HtmlEncode(Base)}/auth/app">
              <input type="hidden" name="key" value="{WebUtility.HtmlEncode(_state.ApiKey)}">
              <input type="hidden" name="returnUrl" value="{WebUtility.HtmlEncode(returnUrl)}">
            </form></body></html>
            """;
        _web.Source = new HtmlWebViewSource { Html = html, BaseUrl = Base + "/" };
    }

    private async Task ShowOfflineAsync()
    {
        _offline = true;
        await using var s = await FileSystem.OpenAppPackageFileAsync("offline.html");
        using var reader = new StreamReader(s);
        _web.Source = new HtmlWebViewSource { Html = await reader.ReadToEndAsync() };
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        var url = e.Url ?? "";
        // The offline screen talks to the app through lifehub-bridge://call?id=…&name=…&arg=…
        if (url.StartsWith("lifehub-bridge:", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            _ = BridgeAsync(new Uri(url));
            return;
        }
        // LifeHub's menu → "Phone app settings".
        if (url.StartsWith("lifehub-app:", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
            MainThread.BeginInvokeOnMainThread(() => Navigation.PushModalAsync(new PhoneSettingsPage(_sp)));
            return;
        }
        // Links to other sites open in the browser.
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !_offline &&
            Uri.TryCreate(url, UriKind.Absolute, out var u) && Uri.TryCreate(Base, UriKind.Absolute, out var b) && u.Host != b.Host)
        {
            e.Cancel = true;
            _ = Launcher.OpenAsync(u);
        }
    }

    private async Task BridgeAsync(Uri uri)
    {
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = q["id"] ?? "";
        var name = q["name"] ?? "";
        var arg = q["arg"] ?? "";
        string reply;
        try
        {
            switch (name)
            {
                case "state": reply = _client.StateJson(); break;
                case "queue": _client.Queue(arg); reply = _client.StateJson(); break;
                case "sync": await _client.SyncAsync(); reply = _client.StateJson(); break;
                case "goOnline":
                    reply = "{}";
                    MainThread.BeginInvokeOnMainThread(async () => await OpenAsync());
                    break;
                case "settings":
                    reply = "{}";
                    MainThread.BeginInvokeOnMainThread(() => Navigation.PushModalAsync(new PhoneSettingsPage(_sp)));
                    break;
                default: reply = "{}"; break;
            }
        }
        catch (Exception ex) { reply = JsonSerializer.Serialize(new { error = ex.Message }); }
        var js = $"window.__lhReply && window.__lhReply({JsonSerializer.Serialize(id)}, {JsonSerializer.Serialize(reply)});";
        await MainThread.InvokeOnMainThreadAsync(() => _web.EvaluateJavaScriptAsync(js));
    }

    protected override bool OnBackButtonPressed()
    {
        if (_web.CanGoBack) { _web.GoBack(); return true; }
        return base.OnBackButtonPressed();
    }
}
