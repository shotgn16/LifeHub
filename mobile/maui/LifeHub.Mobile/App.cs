using LifeHub.Mobile.Mobile;

namespace LifeHub.Mobile;

/// <summary>
/// The app: LifeHub running on this phone (a Blazor page), or a window onto your LifeHub server.
/// The first start asks which (<see cref="Mobile.MobileState.Mode"/>).
/// </summary>
public sealed class App : Application
{
    private readonly IServiceProvider _sp;
    private readonly MobileState _state;

    public App(IServiceProvider sp, MobileState state)
    {
        _sp = sp;
        _state = state;
        MauiProgram.UseThePhonesPerson(sp);
        _state.ModeChanged += () => MainThread.BeginInvokeOnMainThread(ShowMode);
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(PageFor()) { Title = "LifeHub" };

    private Page PageFor() => _state.Mode == MobileMode.Server ? new ServerPage(_sp) : new LifeHubPage();

    private void ShowMode()
    {
        if (Windows.Count > 0) Windows[0].Page = PageFor();
    }

    protected override void OnStart()
    {
        _sp.GetRequiredService<PhoneJobs>().AppOpened();
        base.OnStart();
    }

    protected override void OnResume()
    {
        _sp.GetRequiredService<PhoneJobs>().AppOpened();
        // Connected to a server: the page's live connection dropped while the app was in the background — restore it.
        if (Windows.Count > 0 && Windows[0].Page is ServerPage server) _ = server.ResumedAsync();
        base.OnResume();
    }
}
