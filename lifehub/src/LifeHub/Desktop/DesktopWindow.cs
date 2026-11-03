#if DESKTOP
using System.Drawing;
using Photino.NET;

namespace LifeHub.Desktop;

/// <summary>
/// The LifeHub app window. It decides where LifeHub runs (inside this app, in the background service, in Docker
/// or on another machine), starts it if needed and shows it. The page can ask the window to look again
/// ("restart") after the setup assistant changes how this computer runs LifeHub.
/// </summary>
internal sealed class DesktopWindow
{
    private readonly string _dataDir;
    private readonly DesktopHost.Factory _create;
    private bool _forceSetup;
    private WebApplication? _server;
    private string _serverUrl = "";
    private PhotinoWindow _window = null!;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _lock;

    private DesktopWindow(string dataDir, DesktopHost.Factory create, bool forceSetup)
    {
        _dataDir = dataDir; _create = create; _forceSetup = forceSetup;
    }

    private string UrlFile => Path.Combine(_dataDir, "desktop.url");

    public static int Run(string dataDir, DesktopHost.Factory create, bool forceSetup)
    {
        var w = new DesktopWindow(dataDir, create, forceSetup);
        if (OperatingSystem.IsWindows())
        {
            // The Windows web view needs a single-threaded apartment.
            var t = new Thread(w.Show) { IsBackground = false };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
        }
        else w.Show();      // macOS needs the main thread
        return 0;
    }

    private void Show()
    {
        // A second copy of the app just opens another window onto the LifeHub that's already running.
        string? already = null;
        try { _lock = new FileStream(Path.Combine(_dataDir, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { already = File.Exists(UrlFile) ? File.ReadAllText(UrlFile).Trim() : null; }

        var icon = Path.Combine(AppContext.BaseDirectory, "Desktop", OperatingSystem.IsWindows() ? "lifehub.ico" : "lifehub.png");
        _window = new PhotinoWindow()
            .SetTitle("LifeHub")
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1280, 820))
            .SetMinSize(400, 560)
            .Center()
            .SetResizable(true)
            .SetContextMenuEnabled(true)
            .SetDevToolsEnabled(false)
            .RegisterWebMessageReceivedHandler((_, message) => OnMessage(message));
        if (File.Exists(icon)) _window.SetIconFile(icon);

        if (already is not null && already.Length > 0) _window.Load(new Uri(already));
        else
        {
            _window.LoadRawString(StartingPage("Starting LifeHub…"));
            _window.RegisterWindowCreatedHandler((_, _) => _ = Task.Run(Navigate));
        }
        _window.WaitForClose();

        if (_server is not null) StopServer().GetAwaiter().GetResult();
        _lock?.Dispose();
        if (already is null) try { File.Delete(UrlFile); } catch { }
    }

    private void OnMessage(string message)
    {
        if (message == "restart") _ = Task.Run(Navigate);
        else if (message.StartsWith("open:") && Uri.TryCreate(message[5..], UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            DesktopRuntime.OpenInBrowser(u.ToString());
    }

    private void Load(string url)
    {
        try { File.WriteAllText(UrlFile, url); } catch { }
        _window.Invoke(() => _window.Load(new Uri(url)));
    }

    private void Status(string text) => _window.Invoke(() => _window.LoadRawString(StartingPage(text)));

    /// <summary>Works out where LifeHub is, gets it running and shows it.</summary>
    private async Task Navigate()
    {
        await _gate.WaitAsync();
        try
        {
            var cfg = HostConfig.Load(_dataDir);
            var token = DesktopRuntime.EnsureToken();
            var setup = _forceSetup || !cfg.SetupDone;
            _forceSetup = false;
            var notes = new List<string>();

            // 1. Docker or another machine: just show it.
            if (cfg.SetupDone && !setup && cfg.Mode is HostMode.Docker or HostMode.Remote)
            {
                var url = cfg.Mode == HostMode.Docker ? $"http://localhost:{cfg.Port}" : cfg.RemoteUrl.Trim();
                Status("Connecting to LifeHub…");
                if (cfg.Mode == HostMode.Docker && !await DesktopRuntime.IsLifeHubAsync(url))
                {
                    Status("Starting LifeHub in Docker…");
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("docker", "start " + DockerSetup.Container) { UseShellExecute = false, CreateNoWindow = true })?.WaitForExit(30_000); } catch { }
                    for (var i = 0; i < 20 && !await DesktopRuntime.IsLifeHubAsync(url); i++) await Task.Delay(1500);
                }
                if (await Reachable(url))
                {
                    await StopServer();
                    Load(url);
                    return;
                }
                notes.Add(cfg.Mode == HostMode.Docker ? "docker" : "remote");
                setup = true;
            }

            // 2. The background service (this computer + phone, or household server).
            var wantService = cfg.OnNetwork && cfg.Background && (cfg.SetupDone || cfg.ModeChosen);
            if (wantService)
            {
                var local = $"http://127.0.0.1:{cfg.Port}";
                if (!ServiceManager.IsInstalled() || !await DesktopRuntime.IsLifeHubAsync(local))
                {
                    Status("Setting LifeHub up to run in the background…");
                    await StopServer();
                    var r = ServiceManager.Install(cfg.ServiceScope, _dataDir, cfg.Port);
                    notes.Add("svc=" + Uri.EscapeDataString((r.Ok ? "ok:" : "error:") + r.Message));
                    if (r.Ok)
                        for (var i = 0; i < 30 && !await DesktopRuntime.IsLifeHubAsync(local); i++) await Task.Delay(1000);
                }
                if (await DesktopRuntime.IsLifeHubAsync(local))
                {
                    await StopServer();
                    Load(SignIn(local, token, setup, notes));
                    return;
                }
                notes.Add("svc=" + Uri.EscapeDataString("error:The background service didn't start, so LifeHub runs while this app is open for now."));
            }
            else if (ServiceManager.IsInstalled() && (cfg.SetupDone || cfg.ModeChosen))
            {
                Status("Turning off the background service…");
                ServiceManager.Uninstall();
                await Task.Delay(1500);
            }

            // 3. Inside this app. "This computer only" listens just on this computer; the other modes on the network.
            var onNetwork = cfg.OnNetwork && (cfg.SetupDone || cfg.ModeChosen);
            var bind = onNetwork
                ? $"http://0.0.0.0:{(DesktopRuntime.PortFree(cfg.Port) ? cfg.Port : DesktopRuntime.FreePort())}"
                : $"http://127.0.0.1:{(_server is not null && _serverUrl.StartsWith("http://127.0.0.1") ? new Uri(_serverUrl).Port : DesktopRuntime.FreePort())}";
            if (_server is null || _serverUrl != bind)
            {
                Status("Starting LifeHub…");
                await StopServer();
                _server = _create(Array.Empty<string>(), bind, false);
                await _server.StartAsync();
                _serverUrl = bind;
            }
            Load(SignIn("http://127.0.0.1:" + new Uri(bind).Port, token, setup, notes));
        }
        catch (Exception ex)
        {
            Status("LifeHub couldn't start: " + System.Net.WebUtility.HtmlEncode(ex.Message) +
                   "<br><br><small>Try LifeHub --setup, or see the manual's “Problems” section. Details are in " +
                   System.Net.WebUtility.HtmlEncode(Path.Combine(_dataDir, "lifehub.log")) + "</small>");
            try { File.AppendAllText(Path.Combine(_dataDir, "lifehub.log"), $"{DateTime.Now:u} {ex}\n"); } catch { }
        }
        finally { _gate.Release(); }
    }

    private static string SignIn(string baseUrl, string token, bool setup, List<string> notes)
    {
        var target = setup ? "/welcome" + (notes.Count > 0 ? "?" + string.Join("&", notes.Select(n => n.Contains('=') ? n : "problem=" + n)) : "") : "/";
        return $"{baseUrl}/auth/desktop?token={token}&returnUrl={Uri.EscapeDataString(target)}";
    }

    private static async Task<bool> Reachable(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) return false;
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var r = await c.GetAsync(url);
            return true;            // any answer counts (Home Assistant asks you to sign in first)
        }
        catch { return false; }
    }

    private async Task StopServer()
    {
        if (_server is null) return;
        try { await _server.StopAsync(TimeSpan.FromSeconds(5)); await _server.DisposeAsync(); } catch { }
        _server = null; _serverUrl = "";
    }

    private static string StartingPage(string text) => StartingTemplate.Replace("__TEXT__", text);

    private const string StartingTemplate = """
        <!doctype html><html><head><meta charset="utf-8"><style>
        html,body{height:100%;margin:0;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f6f7f9;color:#1f2933}
        @media (prefers-color-scheme:dark){html,body{background:#14171c;color:#e6e8eb}}
        .c{height:100%;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:18px;text-align:center;padding:24px}
        .m{width:56px;height:56px;border-radius:16px;background:#0f766e;color:#fff;display:flex;align-items:center;justify-content:center;font-size:28px;font-weight:700}
        .s{width:28px;height:28px;border:3px solid #0f766e33;border-top-color:#0f766e;border-radius:50%;animation:r 1s linear infinite}
        @keyframes r{to{transform:rotate(360deg)}}</style></head>
        <body><div class="c"><div class="m">L</div><div>__TEXT__</div><div class="s"></div></div></body></html>
        """;
}
#endif
