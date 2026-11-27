using LifeHub.Modules;
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

// LifeHub starts here. The same code runs as a server (Docker, Home Assistant, IIS, a background service) and,
// in desktop builds (-p:LifeHubDesktop=true), as a desktop app with its own window — see Desktop/DesktopHost.cs.
#if DESKTOP
return LifeHub.Desktop.DesktopHost.Run(args, CreateLifeHub);
#else
// "--server" runs it the way the desktop app's background service does (data folder in your user folder, setup assistant).
if (args.Contains("--server")) return LifeHub.Desktop.DesktopHost.Run(args, CreateLifeHub);
CreateLifeHub(args, null, false).Run();
return 0;
#endif

/// <summary>Builds the LifeHub web app. <paramref name="urls"/> overrides where it listens (desktop app / service).</summary>
static WebApplication CreateLifeHub(string[] args, string? urls, bool windowsService)
{
#if DESKTOP
    // Desktop app: wwwroot sits next to the program, whatever folder it was started from.
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
    if (windowsService) builder.Host.UseWindowsService(o => o.ServiceName = "LifeHub");
#else
    var builder = WebApplication.CreateBuilder(args);
#endif
    if (urls is not null) builder.WebHost.UseUrls(urls);

    // Home Assistant add-on: listen on the ingress port. Plesk/IIS/Docker: ASPNETCORE_URLS or the default.
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")) &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        builder.WebHost.UseUrls("http://0.0.0.0:8099");

    // ---------- Core infrastructure ----------
    builder.Services.AddSingleton<DataPaths>();
    var keysDir = new DataPaths(builder.Configuration, builder.Environment).Keys;
    var protection = builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysDir)).SetApplicationName("LifeHub");
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
    FileDataKeyStore.LockDown(keysDir);
    // Everything stored is encrypted (AES-256-GCM); the key lives in keys/ (or LIFEHUB_DATA_KEY).
    builder.Services.AddSingleton<IDataKeyStore, FileDataKeyStore>();
    builder.Services.AddSingleton(sp =>
    {
        var paths = sp.GetRequiredService<DataPaths>();
        return DataCipher.Open(sp.GetRequiredService<IDataKeyStore>(),
            () => Directory.GetFiles(paths.Db, "*.json").Append(paths.SettingsFile).Any(DataCipher.IsEncryptedFile));
    });
    builder.Services.AddSingleton<LoginThrottle>();
    builder.Services.AddLifeHubCore();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<HaListSyncService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<EmailSyncService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<AutomationService>());
    builder.Services.AddLifeHubModule(new LifeHubModule
    {
        Key = ModuleKeys.Telegram, Title = "Telegram", Icon = "send", Route = "settings?tab=telegram", Section = "AI", Order = 96, InMenu = false,
        Description = "Chat with LifeHub and get notifications in Telegram (Settings → Telegram). Not in the phone app on its own."
    });
    builder.Services.AddSingleton<LifeHub.Integrations.Telegram.TelegramService>();
    builder.Services.AddSingleton<LifeHub.Integrations.Telegram.IChatNotifier>(sp => sp.GetRequiredService<LifeHub.Integrations.Telegram.TelegramService>());
    builder.Services.AddHostedService(sp => sp.GetRequiredService<LifeHub.Integrations.Telegram.TelegramService>());

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

    // 3.1 upgrade: encrypt data files written by earlier versions (each is also encrypted the next time it's saved).
    {
        var paths = app.Services.GetRequiredService<DataPaths>();
        var encrypted = app.Services.GetRequiredService<DataCipher>()
            .EncryptExisting(Directory.GetFiles(paths.Db, "*.json").Append(paths.SettingsFile));
        if (encrypted > 0) app.Logger.LogInformation("Encrypted {Count} data files", encrypted);
    }

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
    return app;
}
