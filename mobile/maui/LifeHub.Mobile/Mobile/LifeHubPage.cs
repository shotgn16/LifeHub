using Microsoft.AspNetCore.Components.WebView.Maui;

namespace LifeHub.Mobile.Mobile;

/// <summary>LifeHub itself, running on this phone (its pages in a Blazor web view).</summary>
public sealed class LifeHubPage : ContentPage
{
    public LifeHubPage()
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(MobileRoot) });
        view.BlazorWebViewInitializing += PlatformWebView.Initializing;
        view.BlazorWebViewInitialized += PlatformWebView.Initialized;
        Content = view;
        Padding = 0;
    }
}
