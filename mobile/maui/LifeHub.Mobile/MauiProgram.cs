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
        var paths = new DataPaths(Path.Combine(FileSystem.AppDataDirectory, "lifehub"));
        builder.Services.AddSingleton(paths);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(paths.Keys)).SetApplicationName("LifeHub");
        builder.Services.AddSingleton<IDataKeyStore, SecureStorageKeyStore>();
        builder.Services.AddSingleton(sp => OpenCipher(paths, sp.GetRequiredService<IDataKeyStore>()));
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

        return builder.Build();
    }

    /// <summary>Everything in the phone app runs as its one person (called once the app is up, and by background checks).</summary>
    public static void UseThePhonesPerson(IServiceProvider sp)
    {
        if (UserScope.Default is not null) return;
        UserScope.Default = new UserScope.State(sp.GetRequiredService<SettingsService>().PrimaryUserId, false);
    }

    private static bool AnyEncrypted(DataPaths paths) =>
        Directory.GetFiles(paths.Db, "*.json").Append(paths.SettingsFile).Any(DataCipher.IsEncryptedFile);

    /// <summary>
    /// The encryption for the data folder. If its key was lost (the keystore entry is gone, e.g. data restored to a new
    /// phone without it), the unreadable data is set aside rather than crashing on every start.
    /// </summary>
    private static DataCipher OpenCipher(DataPaths paths, IDataKeyStore keys)
    {
        if (AnyEncrypted(paths) && !SecureStorageKeyStore.HasKey() && !File.Exists(Path.Combine(paths.Keys, "recovery.key")))
        {
            var aside = paths.Root + "-locked-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            Directory.CreateDirectory(aside);
            foreach (var dir in new[] { paths.Db, paths.Backups, paths.Keys })
                if (Directory.Exists(dir)) Directory.Move(dir, Path.Combine(aside, Path.GetFileName(dir)));
            if (File.Exists(paths.SettingsFile)) File.Move(paths.SettingsFile, Path.Combine(aside, "settings.json"));
            _ = new DataPaths(paths.Root);    // recreate the empty folders
        }
        return DataCipher.Open(keys, () => AnyEncrypted(paths));
    }
}
