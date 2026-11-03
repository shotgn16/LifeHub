namespace LifeHub.Desktop;

/// <summary>
/// The desktop app's entry point. One program does everything:
/// <code>
///   LifeHub                         the app window (first run: the setup assistant)
///   LifeHub --setup                 the app window, straight to the setup assistant
///   LifeHub --server                LifeHub without a window (what the background service runs)
///   LifeHub --uninstall-service     removes the background service (used by the uninstallers)
///   --data &lt;folder&gt;  --port &lt;n&gt;     where the data lives / which port to use
/// </code>
/// </summary>
public static class DesktopHost
{
    public delegate WebApplication Factory(string[] args, string? urls, bool windowsService);

    public static int Run(string[] args, Factory create)
    {
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        bool Has(string name) => args.Contains(name);

        var dataDir = Path.GetFullPath(Arg("--data") ?? Environment.GetEnvironmentVariable("LIFEHUB_DATA") ?? DesktopPaths.DefaultData());
        Directory.CreateDirectory(dataDir);
        Environment.SetEnvironmentVariable("LIFEHUB_DATA", dataDir);
        DesktopRuntime.Active = true;
        DesktopRuntime.DataDir = dataDir;
        var cfg = HostConfig.Load(dataDir);
        var port = int.TryParse(Arg("--port"), out var p) && p is > 0 and < 65536 ? p : cfg.Port;

        if (Has("--install-system-service")) return ServiceManager.WindowsServiceAdmin(true, dataDir, port);
        if (Has("--remove-system-service")) return ServiceManager.WindowsServiceAdmin(false, dataDir, port);
        if (Has("--uninstall-service")) return ServiceManager.Uninstall().Ok ? 0 : 1;

        if (Has("--server"))
        {
            DesktopRuntime.IsService = true;
            DesktopRuntime.EnsureToken();
            var app = create(Array.Empty<string>(), $"http://0.0.0.0:{port}", Has("--windows-service"));
            app.Run();
            return 0;
        }

#if DESKTOP
        return DesktopWindow.Run(dataDir, create, Has("--setup"));
#else
        Console.Error.WriteLine("This build has no app window. Start it with --server, or build with -p:LifeHubDesktop=true.");
        return 2;
#endif
    }
}
