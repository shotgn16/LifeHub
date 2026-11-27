# Extending LifeHub

Everything is plain C#, and most extension points are a single interface plus one line in
`Program.cs`. To change how LifeHub *looks*, see [Restyle it](#restyle-it) — or, without touching
code, [Make it yours](manual/customise.md).

## Give the AI a new ability

Implement `IAiActionHandler` and register it. Its `Type` and `Instructions` are added to the
model's prompt and JSON schema automatically. It also appears in the AI Inbox and gets an
**Auto-apply** checkbox in Settings → AI.

```csharp
// lifehub/src/LifeHub/Ai/Handlers/ParcelActionHandler.cs
namespace LifeHub.Ai.Handlers;

public sealed class ParcelActionHandler : IAiActionHandler
{
    private readonly TaskService _tasks;
    public ParcelActionHandler(TaskService tasks) => _tasks = tasks;

    public string Type => "parcel";
    public string Label => "Parcel delivery";
    public string Instructions =>
        "A parcel/delivery notification. title = what is arriving and from whom, date = expected delivery date.";

    public async Task<string> ApplyAsync(AiSuggestion s, CancellationToken ct)
    {
        var task = new TaskItem { Title = "📦 " + s.Title, DueDate = s.Date, Category = "Errands", Source = s.Origin };
        await _tasks.SaveAsync(task);
        s.Extra["undo"] = $"task:{task.Id}";   // optional: makes it undoable from AI Inbox → History
        return "Delivery tracked as a task";
    }
}
```

```csharp
// Program.cs
builder.Services.AddSingleton<IAiActionHandler, ParcelActionHandler>();
```

Handlers can use any service. For example, `HomeAssistantClient.CallServiceAsync` can run a
script or scene, and `CalendarService` and `KitchenService` are available too. Free-form extra
fields can go in `AiSuggestion.Extra`.

If an action should only come from something the user typed or said (Ask LifeHub, quick capture,
Telegram) and never from an email, add the marker interface `IUserOnlyAction`
(`class MyHandler : IAiActionHandler, IUserOnlyAction`). LifeHub's own schedule, memory and
tick-off actions use it, so an email's sender can't change someone's schedule or memory.

Ask LifeHub (`AskService`) gives the model a compact picture of the person's lists and week
(`AskService.ContextAsync`) and the facts in **AI Inbox → Memory** (`MemoryService.PromptBlock`).
Its suggestions are always saved for review, never applied automatically.

## Add a mail system

1. Implement `IEmailProvider`:
   - `FetchNewAsync`
   - `TestAsync`
   - `MoveToTrashAsync`
   - `GetPresenceAsync`
2. Add a value to `EmailAccountKind`.
3. Register the provider in `Program.cs`.
4. Add its fields to `Components/SettingsTabs/SettingsEmail.razor`.

## Add a module (a new section)

Every section of LifeHub — its menu entry, home-screen card, badge and bottom-bar button — is a
**module**, registered in one place: `AddLifeHubModules()` in
[`Modules/ModuleRegistry.cs`](../lifehub/src/LifeHub/Modules/ModuleRegistry.cs). Admins can switch
modules on and off in **Settings → Modules**; each person arranges the cards on their home screen.

Here's a complete "Habits" module.

**1. The data.** A class deriving from `Entity` in `Domain/`. `IRepository<Habit>` is available
straight away, stored as `data/db/Habit.json`. Give it an ownership scope so it works with several
people:

```csharp
[Owned(OwnScope.Personal)]       // or Kitchen / Meals / Renewals (shared per household setting) / Global
public sealed class Habit : Entity
{
    public string Name { get; set; } = "";
    public List<DateOnly> Done { get; set; } = new();
}
```

Reads only return what the current person may see, and new records are stamped with their owner.
Background jobs run as *system*; for per-person work wrap it in `using (UserScope.As(user.Id)) { … }`,
looping over `SettingsService.Users`.

**2. The page.** `Components/Pages/HabitsPage.razor`:

```razor
@page "/habits"
@inherits LiveComponent
@inject IRepository<Habit> Habits

<h1>Habits</h1>
@foreach (var h in list) { <div class="card">@h.Name</div> }

@code {
    private List<Habit> list = new();
    protected override void OnInitialized() { Watch(Habits); Load(); }   // refreshes when data changes
    protected override void OnDataChanged() => Load();
    private void Load() => list = Habits.All().ToList();
}
```

**3. A home-screen card (optional).** `Components/Widgets/HabitsWidget.razor`, using the shared
`WidgetCard` frame:

```razor
@inherits LiveComponent
@inject IRepository<Habit> Habits

<WidgetCard Title="Habits" Icon="check" Link="habits" LinkText="All habits">
    <ul class="list">@foreach (var h in Habits.All().Take(4)) { <li class="row">@h.Name</li> }</ul>
</WidgetCard>

@code { protected override void OnInitialized() => Watch(Habits); }
```

**4. Register it** — one line in `AddLifeHubModules()` (or in `Program.cs` with
`builder.Services.AddLifeHubModule(...)`):

```csharp
services.AddLifeHubModule(new LifeHubModule
{
    Key = "habits", Title = "Habits", Icon = "check", Route = "habits",
    Section = "Organise", Order = 35,
    Description = "Daily habits and streaks.",
    Widget = typeof(HabitsWidget),
    Badge = sp => 0,                       // number shown next to the menu entry
});
```

That's it: Habits appears in the menu, in **Settings → Modules**, and as a card people can add in
**Customise**. Other options: `WideWidget` (card spans two columns), `BottomBar` (phone bottom
bar), `Available` (e.g. only when Home Assistant is connected), `Requires` (depends on another
module), `Core` (can't be switched off). Icons are the names in
`Components/Shared/Icon.razor`. To leave a built-in feature out of your build entirely, delete its
line in `AddLifeHubModules()`.

## Restyle it

- **Design tokens** — colours, corners, spacing and fonts — are CSS variables at the top of
  [`wwwroot/css/theme.css`](../lifehub/src/LifeHub/wwwroot/css/theme.css), with light and dark
  sets. `<html>` carries `data-theme` (`light`/`dark`, absent for automatic) and `data-density`
  (`compact`). Change the tokens there to restyle the whole app.
- **Components** use those tokens in `wwwroot/app.css`. Every home-screen card is drawn by
  `Components/Widgets/WidgetCard.razor` (`.card.widget`), so restyling cards is one place.
- **Without rebuilding:** **Settings → Appearance → Custom CSS**, or `custom.css` in the data
  folder, is loaded after everything else (see [customise](manual/customise.md)).
- **Per person:** theme, accent colour (applied with `color-mix`), density and the home-screen
  layout live on `UserProfile`.

## Swap the storage

`JsonRepository<T>` handles thousands of records comfortably. To use SQLite or SQL Server:

1. Write an EF Core implementation of `IRepository<T>`.
2. Replace this registration with your implementation:

```csharp
builder.Services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
```

## Publish more sensors to Home Assistant

Add to `AutomationService.PublishSensorsAsync`. Each `SetStateAsync` call creates one entity.

## Releasing a new version

1. **Build the image first.** Go to Actions → **Add-on image** → Run workflow, and enter the new
   version. This publishes `ghcr.io/shotgn16/lifehub-{amd64,aarch64}:X.Y.Z` and the Docker image
   `ghcr.io/shotgn16/lifehub:X.Y.Z` (and `:latest`). Home Assistant
   downloads this image, so it must exist before HA offers the update.
   - On a first publish, check the packages are **Public**: your GitHub profile → Packages →
     Package settings.
2. Bump `<Version>` in `lifehub/src/LifeHub/LifeHub.csproj` and `version:` in
   `lifehub/config.yaml`.
3. For the phone app (only when `mobile/` changed): bump `ApplicationDisplayVersion` (and
   `ApplicationVersion`) in `mobile/maui/LifeHub.Mobile/LifeHub.Mobile.csproj`.
   New offline change types need adding in three places: `SyncService.ApplyOneAsync`, the
   `view()`/`queue()` code in `mobile/shared/offline.html`, and the docs. Older apps skip types they
   don't know.
4. Add an entry to both `CHANGELOG.md` (root) and `lifehub/CHANGELOG.md`.
5. Commit, tag `vX.Y.Z` and push the tag. The release workflow builds the desktop installers and
   phone apps and creates the GitHub release with all of them, and Home Assistant offers the update
   to everyone who added the repository.
