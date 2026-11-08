using LifeHub.Components.Shared;
using LifeHub.Integrations.Ollama;
using LifeHub.Mobile.Mobile;
using LifeHub.Modules;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace LifeHub.Mobile;

/// <summary>Builds the phone app: LifeHub's own services on this phone, plus what only a phone has.</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // ---- LifeHub on this phone: data in the app's private storage, encrypted with a key in the phone's keystore ----
        var paths = OpenDataFolder();
        builder.Services.AddSingleton(paths);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(paths.Keys)).SetApplicationName("LifeHub");
        builder.Services.AddSingleton<IDataKeyStore, SecureStorageKeyStore>();
        builder.Services.AddSingleton(sp => DataCipher.Open(sp.GetRequiredService<IDataKeyStore>(), () => AnyEncrypted(paths)));
        builder.Services.AddLifeHubCore();

        // ---- One person, no sign-in ----
        builder.Services.AddAuthorizationCore();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<AuthenticationStateProvider, DeviceAuthStateProvider>();
        builder.Services.AddScoped<UiState>();
        builder.Services.AddScoped<UserContext>();

        // ---- What only the phone app has ----
        builder.Services.AddSingleton<MobileState>();
        builder.Services.AddSingleton<OnDeviceAi>();
        builder.Services.AddSingleton<IOnDeviceAi>(sp => sp.GetRequiredService<OnDeviceAi>());
        builder.Services.AddSingleton<IDeviceNotifier, DeviceNotifier>();
        builder.Services.AddSingleton<ServerClient>();
        builder.Services.AddSingleton<PhoneJobs>();
        builder.Services.AddLifeHubModule(new LifeHubModule
        {
            Key = "phone", Title = "This phone", Icon = "phone", Route = "phone", Section = "Phone", Order = 900, Core = true,
            Description = "Background checks, notifications, the on-device AI and how this app runs."
        });

        var app = builder.Build();
        // Everything in the phone app runs as its one person.
        var settings = app.Services.GetRequiredService<SettingsService>();
        UserScope.Default = new UserScope.State(settings.PrimaryUserId, false);
        return app;
    }

    private static bool AnyEncrypted(DataPaths paths) =>
        Directory.GetFiles(paths.Db, "*.json").Append(paths.SettingsFile).Any(DataCipher.IsEncryptedFile);

    /// <summary>
    /// The data folder. If its key was lost (the app's keystore entry is gone, e.g. data restored to a new phone without
    /// it), the unreadable folder is set aside rather than crashing on every start.
    /// </summary>
    private static DataPaths OpenDataFolder()
    {
        var root = Path.Combine(FileSystem.AppDataDirectory, "lifehub");
        var paths = new DataPaths(root);
        if (AnyEncrypted(paths) && !SecureStorageKeyStore.HasKey() && !File.Exists(Path.Combine(paths.Keys, "recovery.key")))
        {
            Directory.Move(root, root + "-locked-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            paths = new DataPaths(root);
        }
        return paths;
    }
}
