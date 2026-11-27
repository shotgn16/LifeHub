namespace LifeHub.Modules;

/// <summary>
/// One feature area of LifeHub (Tasks, Kitchen, Notes…). The menu, the phone bottom bar and the home screen are all
/// built from the registered modules, and each can be switched off in Settings → Modules.
///
/// To add your own: create a page (<c>@page "/mything"</c>), optionally a home-screen card component, and register it in
/// Program.cs:
/// <code>
/// builder.Services.AddLifeHubModule(new LifeHubModule { Key = "mything", Title = "My thing", Icon = "star",
///     Route = "mything", Section = "Organise", Order = 45, Widget = typeof(MyThingWidget) });
/// </code>
/// </summary>
public sealed class LifeHubModule
{
    /// <summary>Stable id, used in settings (e.g. "tasks").</summary>
    public required string Key { get; init; }
    public required string Title { get; init; }
    /// <summary>Icon name from Components/Shared/Icon.razor.</summary>
    public required string Icon { get; init; }
    /// <summary>Page address relative to LifeHub, e.g. "tasks" ("" for the home screen).</summary>
    public required string Route { get; init; }
    /// <summary>Menu group: "Organise", "Home & food", "Connected".</summary>
    public string Section { get; init; } = "Organise";
    /// <summary>One line shown in Settings → Modules.</summary>
    public string Description { get; init; } = "";
    /// <summary>Position in the menu and on the home screen (lower first).</summary>
    public int Order { get; init; } = 50;
    /// <summary>Always on (the home screen and settings).</summary>
    public bool Core { get; init; }
    /// <summary>Card shown on the home screen (a Razor component), or null.</summary>
    public Type? Widget { get; init; }
    /// <summary>The card spans two columns on wide screens.</summary>
    public bool WideWidget { get; init; }
    /// <summary>Shown in the phone's bottom bar (keep it to three or four).</summary>
    public bool BottomBar { get; init; }
    /// <summary>A number next to the menu entry (e.g. tasks due today), or null for none.</summary>
    public Func<IServiceProvider, int>? Badge { get; init; }
    /// <summary>Only shown when this returns true (e.g. only when Home Assistant is connected).</summary>
    public Func<IServiceProvider, bool>? Available { get; init; }
    /// <summary>
    /// False for a feature without a page of its own (Ask LifeHub, Memory, Telegram): it can still be switched off in
    /// Settings → Modules, but it isn't in the menu.
    /// </summary>
    public bool InMenu { get; init; } = true;
    /// <summary>Another module this one needs (e.g. Scan needs Kitchen).</summary>
    public string? Requires { get; init; }
}
