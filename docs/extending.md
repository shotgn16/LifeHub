# Extending LifeHub

Everything is plain C#, and most extension points are a single interface plus one line in
`Program.cs`.

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

## Add a mail system

1. Implement `IEmailProvider`:
   - `FetchNewAsync`
   - `TestAsync`
   - `MoveToTrashAsync`
   - `GetPresenceAsync`
2. Add a value to `EmailAccountKind`.
3. Register the provider in `Program.cs`.
4. Add its fields to `Components/SettingsTabs/SettingsEmail.razor`.

## Add a page or module

1. Add a class deriving from `Entity` in `Domain/`. `IRepository<YourType>` is available
   straight away, stored as `data/db/YourType.json`.
2. Add a service in `Services/` and register it in `Program.cs`.
3. Add `Components/Pages/YourPage.razor` with `@page "/your-page"`. Inherit `LiveComponent` and
   call `Watch(repo)` so the page refreshes when data changes.
4. Add a `NavLink` in `Components/Layout/MainLayout.razor`.

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

1. Bump `<Version>` in `lifehub/src/LifeHub/LifeHub.csproj` and `version:` in
   `lifehub/config.yaml`.
2. Add an entry to both `CHANGELOG.md` (root) and `lifehub/CHANGELOG.md`.
3. Commit, tag `vX.Y.Z` and push the tag. The release workflow creates the GitHub release, and
   Home Assistant offers the update to everyone who added the repository.
