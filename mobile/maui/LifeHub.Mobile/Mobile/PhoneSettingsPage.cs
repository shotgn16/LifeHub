using Microsoft.AspNetCore.Components.WebView.Maui;

namespace LifeHub.Mobile.Mobile;

/// <summary>"This phone" settings on their own (opened from a LifeHub server's menu in server mode).</summary>
public sealed class PhoneSettingsPage : ContentPage
{
    public PhoneSettingsPage(IServiceProvider sp)
    {
        var view = new BlazorWebView { HostPage = "wwwroot/index.html" };
        view.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(PhoneSettingsRoot) });
        Content = view;
    }
}
