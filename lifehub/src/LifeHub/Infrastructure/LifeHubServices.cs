using LifeHub.Ai;
using LifeHub.Ai.Handlers;
using LifeHub.Integrations.Email;
using LifeHub.Integrations.HomeAssistant;
using LifeHub.Integrations.Ollama;
using LifeHub.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace LifeHub.Infrastructure;

/// <summary>
/// Everything LifeHub is made of, shared by every host: the web server (Program.cs) and the phone app
/// (mobile/maui). Each host adds its own data folder, key store, sign-in and how background jobs are started.
/// </summary>
public static class LifeHubServices
{
    public static IServiceCollection AddLifeHubCore(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();
        services.AddSingleton<Clock>();
        services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
        services.AddMemoryCache();
        services.AddHttpClient();

        // ---------- Domain services ----------
        services.AddSingleton<TaskService>();
        services.AddSingleton<RenewalService>();
        services.AddSingleton<KitchenService>();
        services.AddSingleton<BarcodeService>();
        services.AddSingleton<EmailLinkService>();
        services.AddSingleton<CalendarService>();
        services.AddSingleton<ScheduleService>();
        services.AddSingleton<PlannerService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<BackgroundStatus>();
        services.AddSingleton<WebPushService>();
        services.AddSingleton<KitchenPhotoService>();
        services.AddSingleton<MealService>();
        services.AddSingleton<PeopleService>();
        services.AddSingleton<NotesService>();
        services.AddSingleton<MemoryService>();
        services.AddSingleton<AskService>();
        services.AddSingleton<DealsService>();
        services.AddSingleton<SyncService>();
        services.AddSingleton<BibleArchiveService>();

        // ---------- Modules (menu, home screen cards; add your own here — see docs/extending.md) ----------
        services.AddLifeHubModules();

        // ---------- Integrations ----------
        services.AddSingleton<HomeAssistantClient>();
        services.AddSingleton<HomeAssistantCatalog>();
        services.AddSingleton<HomeAssistantSetup>();
        services.AddSingleton<HaListSyncService>();
        services.AddSingleton<AiClient>();
        services.AddSingleton<MicrosoftGraphClient>();
        services.AddSingleton<IEmailProvider, ImapEmailProvider>();
        services.AddSingleton<IEmailProvider, GraphEmailProvider>();
        services.AddSingleton<IEmailProvider, EwsEmailProvider>();

        // ---------- AI actions (add your own IAiActionHandler here to extend what the AI can do) ----------
        services.AddSingleton<IAiActionHandler, TaskActionHandler>();
        services.AddSingleton<IAiActionHandler, EventActionHandler>();
        services.AddSingleton<IAiActionHandler, RenewalActionHandler>();
        services.AddSingleton<IAiActionHandler, ShoppingActionHandler>();
        services.AddSingleton<IAiActionHandler, PantryActionHandler>();
        services.AddSingleton<IAiActionHandler, PromotionActionHandler>();
        services.AddSingleton<IAiActionHandler, ScheduleActionHandler>();
        services.AddSingleton<IAiActionHandler, MemoryActionHandler>();
        services.AddSingleton<IAiActionHandler, DoneActionHandler>();
        services.AddSingleton<AiService>();

        // ---------- Background jobs ----------
        services.AddSingleton<EmailSyncService>();
        services.AddSingleton<AutomationService>();

        return services;
    }
}
