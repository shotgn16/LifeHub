using LifeHub.Components.Widgets;

namespace LifeHub.Modules;

/// <summary>The registered modules, and which are switched on (household setting, Settings → Modules).</summary>
public sealed class ModuleRegistry
{
    private readonly IReadOnlyList<LifeHubModule> _all;
    private readonly SettingsService _settings;
    private readonly IServiceProvider _sp;

    public ModuleRegistry(IEnumerable<LifeHubModule> modules, SettingsService settings, IServiceProvider sp)
    {
        _all = modules.GroupBy(m => m.Key).Select(g => g.Last()).OrderBy(m => m.Order).ThenBy(m => m.Title).ToList();
        _settings = settings; _sp = sp;
    }

    public IReadOnlyList<LifeHubModule> All => _all;

    public bool IsOn(string key)
    {
        var m = _all.FirstOrDefault(x => x.Key == key);
        if (m is null) return false;
        if (m.Core) return true;
        var off = _settings.Household.General.DisabledModules;
        if (off.Contains(m.Key)) return false;
        return m.Requires is null || IsOn(m.Requires);
    }

    /// <summary>Switched on, and available right now (e.g. Home only when Home Assistant is connected).</summary>
    public IReadOnlyList<LifeHubModule> Enabled() =>
        _all.Where(m => IsOn(m.Key) && (m.Available?.Invoke(_sp) ?? true)).ToList();

    public int BadgeOf(LifeHubModule m)
    {
        try { return m.Badge?.Invoke(_sp) ?? 0; } catch { return 0; }
    }

    /// <summary>Home screen cards for the current person, in their order, without the ones they hid.</summary>
    public IReadOnlyList<LifeHubModule> Widgets(UserProfile? me)
    {
        var list = Enabled().Where(m => m.Widget is not null).ToList();
        if (me is null) return list;
        var order = me.DashboardOrder;
        return list.Where(m => !me.DashboardHidden.Contains(m.Key))
            .OrderBy(m => order.IndexOf(m.Key) is var i && i >= 0 ? i : 1000 + m.Order).ToList();
    }
}

public static class ModuleServiceExtensions
{
    public static IServiceCollection AddLifeHubModule(this IServiceCollection services, LifeHubModule module) =>
        services.AddSingleton(module);

    /// <summary>LifeHub's own modules. Remove a line to leave a feature out of your build entirely.</summary>
    public static IServiceCollection AddLifeHubModules(this IServiceCollection services)
    {
        services.AddSingleton<ModuleRegistry>();
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "today", Title = "Today", Icon = "today", Route = "", Order = 0, Core = true, BottomBar = true,
            Description = "Your home screen: a card from each module."
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "tasks", Title = "Tasks", Icon = "tasks", Route = "tasks", Order = 10, BottomBar = true,
            Description = "To-dos with due dates, priorities, repeats and checklists.", Widget = typeof(TasksWidget), WideWidget = true,
            Badge = sp => sp.GetRequiredService<TaskService>().DueToday().Count
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "schedule", Title = "Schedule", Icon = "calendar", Route = "schedule", Order = 20,
            Description = "Your week of work and free time, calendars, and Plan my day.", Widget = typeof(AgendaWidget)
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "notes", Title = "Notes", Icon = "note", Route = "notes", Order = 30,
            Description = "Quick notes the AI sorts into categories.", Widget = typeof(NotesWidget)
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "renewals", Title = "Renewals", Icon = "renewals", Route = "renewals", Order = 40,
            Description = "Subscriptions, domains, insurance and bills, with reminders.", Widget = typeof(RenewalsWidget)
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "kitchen", Title = "Kitchen", Icon = "kitchen", Route = "kitchen", Section = "Home & food", Order = 50,
            Description = "Fridge, freezer and cupboard stock with use-by dates.", Widget = typeof(KitchenWidget)
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "scan", Title = "Scan", Icon = "barcode", Route = "scan", Section = "Home & food", Order = 55, Requires = "kitchen",
            Description = "Barcode scanner for stocking up, using up and shopping."
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "shopping", Title = "Shopping", Icon = "cart", Route = "shopping", Section = "Home & food", Order = 60, BottomBar = true,
            Description = "The shopping list, shared with Home Assistant if you like.", Widget = typeof(ShoppingWidget),
            Badge = sp => sp.GetRequiredService<KitchenService>().Shopping.Where(s => !s.Checked).Count
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "meals", Title = "Meals", Icon = "pot", Route = "meals", Section = "Home & food", Order = 70,
            Description = "Meal planner, recipes, what to buy and local deals.", Widget = typeof(MealsWidget)
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "home", Title = "Home", Icon = "home", Route = "home", Section = "Connected", Order = 80,
            Description = "Rooms, lights, who's home and energy from Home Assistant.", Widget = typeof(HomeWidget),
            Available = sp => sp.GetRequiredService<LifeHub.Integrations.HomeAssistant.HomeAssistantClient>().IsConfigured
        });
        services.AddLifeHubModule(new LifeHubModule
        {
            Key = "inbox", Title = "AI Inbox", Icon = "inbox", Route = "inbox", Section = "Connected", Order = 90,
            Description = "Your email, read by your own AI and turned into tasks, events and renewals.", Widget = typeof(InboxWidget),
            Badge = sp => sp.GetRequiredService<LifeHub.Ai.AiService>().PendingCount
        });
        return services;
    }
}
