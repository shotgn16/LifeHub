using LifeHub.Ai;
using LifeHub.Ai.Handlers;
using LifeHub.Api;
using LifeHub.Components;
using LifeHub.Integrations.Email;
using LifeHub.Integrations.HomeAssistant;
using LifeHub.Integrations.Ollama;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Home Assistant add-on: listen on the ingress port. Plesk/IIS/Docker: ASPNETCORE_URLS or the default.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    builder.WebHost.UseUrls("http://0.0.0.0:8099");

// ---------- Core infrastructure ----------
builder.Services.AddSingleton<DataPaths>();
var keysDir = new DataPaths(builder.Configuration, builder.Environment).Keys;
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDir)).SetApplicationName("LifeHub");
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<Clock>();
builder.Services.AddSingleton(typeof(IRepository<>), typeof(JsonRepository<>));
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// ---------- Domain services ----------
builder.Services.AddSingleton<TaskService>();
builder.Services.AddSingleton<RenewalService>();
builder.Services.AddSingleton<KitchenService>();
builder.Services.AddSingleton<BarcodeService>();
builder.Services.AddSingleton<EmailLinkService>();
builder.Services.AddSingleton<CalendarService>();
builder.Services.AddSingleton<ScheduleService>();
builder.Services.AddSingleton<PlannerService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddSingleton<WebPushService>();
builder.Services.AddSingleton<BibleService>();
builder.Services.AddSingleton<BibleStudyService>();
builder.Services.AddSingleton<KitchenPhotoService>();
builder.Services.AddSingleton<MealService>();
builder.Services.AddSingleton<PeopleService>();
builder.Services.AddSingleton<NotesService>();
builder.Services.AddSingleton<DealsService>();

// ---------- Integrations ----------
builder.Services.AddSingleton<HomeAssistantClient>();
builder.Services.AddSingleton<HomeAssistantCatalog>();
builder.Services.AddSingleton<HomeAssistantSetup>();
builder.Services.AddSingleton<HaListSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<HaListSyncService>());
builder.Services.AddSingleton<OllamaClient>();
builder.Services.AddSingleton<MicrosoftGraphClient>();
builder.Services.AddSingleton<IEmailProvider, ImapEmailProvider>();
builder.Services.AddSingleton<IEmailProvider, GraphEmailProvider>();
builder.Services.AddSingleton<IEmailProvider, EwsEmailProvider>();

// ---------- AI actions (add your own IAiActionHandler here to extend what the AI can do) ----------
builder.Services.AddSingleton<IAiActionHandler, TaskActionHandler>();
builder.Services.AddSingleton<IAiActionHandler, EventActionHandler>();
builder.Services.AddSingleton<IAiActionHandler, RenewalActionHandler>();
builder.Services.AddSingleton<IAiActionHandler, ShoppingActionHandler>();
builder.Services.AddSingleton<IAiActionHandler, PantryActionHandler>();
builder.Services.AddSingleton<IAiActionHandler, PromotionActionHandler>();
builder.Services.AddSingleton<AiService>();

// ---------- Background jobs ----------
builder.Services.AddSingleton<EmailSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailSyncService>());
builder.Services.AddSingleton<AutomationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationService>());
builder.Services.AddSingleton<BibleTrackingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BibleTrackingService>());

// ---------- Auth ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "lifehub.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 401; return Task.CompletedTask; }
            var returnUrl = ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString;
            ctx.Response.Redirect($"{ctx.Request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return Task.CompletedTask;
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(AuthConstants.ApiKeyScheme, null);

// Pages require sign-in via @attribute [Authorize] in Components/_Imports.razor
// (login/setup opt out with [AllowAnonymous]); API endpoints declare their own policies.
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    // Behind Plesk's nginx / IIS / any reverse proxy.
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = null;   // Cloudflare → cloudflared → LifeHub adds more than one hop
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 1024 * 1024);   // photo uploads stream in chunks
builder.Services.AddScoped<LifeHub.Components.Shared.UiState>();
builder.Services.AddScoped<UserContext>();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, UserCircuitHandler>();

var app = builder.Build();

app.UseMiddleware<IngressMiddleware>();       // first: needs the real proxy IP, and sets PathBase before routing
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<SetupAndIngressAuthMiddleware>();
app.UseAuthorization();
app.UseMiddleware<UserScopeMiddleware>();     // pages and API calls run as the signed-in person
app.UseAntiforgery();

app.MapLifeHubEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Logger.LogInformation("LifeHub data folder: {Path}", app.Services.GetRequiredService<DataPaths>().Root);
app.Run();
