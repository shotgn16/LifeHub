using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;

namespace LifeHub.Desktop;

/// <summary>
/// Checks for Ollama (the free program that runs LifeHub's AI on this computer), installs it if you agree,
/// and downloads a model that suits this computer's memory.
/// </summary>
public static class OllamaSetup
{
    public const string Url = "http://localhost:11434";

    public sealed record Status(bool Running, IReadOnlyList<string> Models);

    public static async Task<Status> CheckAsync()
    {
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var j = await c.GetFromJsonAsync<JsonElement>(Url + "/api/tags");
            var models = j.TryGetProperty("models", out var m) ? m.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "").Where(x => x.Length > 0).ToList() : new List<string>();
            return new(true, models);
        }
        catch { return new(false, Array.Empty<string>()); }
    }

    /// <summary>Installed but not running (e.g. just installed, or quit from the tray)?</summary>
    public static string? InstalledPath()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama app.exe"),
                      Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe") }
            : OperatingSystem.IsMacOS()
                ? new[] { "/Applications/Ollama.app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Ollama.app") }
                : new[] { "/usr/local/bin/ollama", "/usr/bin/ollama", "/bin/ollama" };
        return candidates.FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
    }

    /// <summary>A model that fits: the 7–8B models need about 8 GB of memory; smaller computers get a 3B model.</summary>
    public static (string Model, string Why) Recommended()
    {
        var gb = DesktopRuntime.MemoryGb();
        if (gb >= 15) return ("qwen2.5:7b", $"This computer has {gb:0} GB of memory, enough for the recommended model (about 4.7 GB to download).");
        if (gb >= 7) return ("qwen2.5:3b", $"This computer has {gb:0} GB of memory, so a smaller, faster model is a better fit (about 1.9 GB to download).");
        return ("qwen2.5:1.5b", $"This computer has {gb:0} GB of memory, so the smallest model is the best fit (about 1 GB to download). It's less accurate.");
    }

    public static bool CanInstall => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <summary>Downloads and installs Ollama, then starts it. Reports progress as text.</summary>
    public static async Task InstallAsync(IProgress<string> progress, CancellationToken ct)
    {
        if (InstalledPath() is null)
        {
            if (OperatingSystem.IsWindows())
            {
                var file = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
                await DownloadAsync("https://ollama.com/download/OllamaSetup.exe", file, progress, ct);
                progress.Report("Installing Ollama…");
                await RunAsync(file, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", ct);
            }
            else if (OperatingSystem.IsMacOS())
            {
                var zip = Path.Combine(Path.GetTempPath(), "Ollama-darwin.zip");
                await DownloadAsync("https://ollama.com/download/Ollama-darwin.zip", zip, progress, ct);
                progress.Report("Installing Ollama in Applications…");
                var target = Directory.Exists("/Applications") && CanWrite("/Applications") ? "/Applications"
                           : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications");
                Directory.CreateDirectory(target);
                await RunAsync("/usr/bin/ditto", $"-x -k \"{zip}\" \"{target}\"", ct);
            }
            else
            {
                progress.Report("Installing Ollama (your computer will ask for your password)…");
                var script = Path.Combine(Path.GetTempPath(), "ollama-install.sh");
                await DownloadAsync("https://ollama.com/install.sh", script, progress, ct);
                var code = await RunAsync("pkexec", $"sh \"{script}\"", ct);
                if (code != 0) throw new InvalidOperationException("The Ollama install didn't finish (it needs your password). You can run this in a terminal instead: curl -fsSL https://ollama.com/install.sh | sh");
            }
        }
        await StartAsync(progress, ct);
    }

    public static async Task StartAsync(IProgress<string> progress, CancellationToken ct)
    {
        if ((await CheckAsync()).Running) return;
        progress.Report("Starting Ollama…");
        var path = InstalledPath();
        try
        {
            if (OperatingSystem.IsMacOS() && path is not null) Process.Start("open", $"-a \"{path}\"");
            else if (OperatingSystem.IsWindows() && path is not null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else if (path is not null) Process.Start(new ProcessStartInfo(path, "serve") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch { }
        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(1500, ct);
            if ((await CheckAsync()).Running) return;
        }
        throw new InvalidOperationException("Ollama was installed but didn't start. Open it once from your applications, then press Check again.");
    }

    /// <summary>Downloads a model, reporting progress (Ollama's /api/pull stream).</summary>
    public static async Task PullAsync(string model, IProgress<(string Status, double? Percent)> progress, CancellationToken ct)
    {
        using var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var req = new HttpRequestMessage(HttpMethod.Post, Url + "/api/pull") { Content = JsonContent.Create(new { model, stream = true }) };
        using var r = await c.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await r.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0) continue;
            using var j = JsonDocument.Parse(line);
            var root = j.RootElement;
            if (root.TryGetProperty("error", out var err)) throw new InvalidOperationException(err.GetString());
            var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
            double? pct = root.TryGetProperty("total", out var t) && t.GetInt64() > 0 && root.TryGetProperty("completed", out var done)
                ? done.GetInt64() * 100.0 / t.GetInt64() : null;
            progress.Report((status, pct));
        }
    }

    private static bool CanWrite(string dir)
    {
        try { var f = Path.Combine(dir, ".lifehub-test"); File.WriteAllText(f, ""); File.Delete(f); return true; }
        catch { return false; }
    }

    private static async Task DownloadAsync(string url, string file, IProgress<string> progress, CancellationToken ct)
    {
        using var c = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LifeHub-Desktop");
        using var r = await c.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        var total = r.Content.Headers.ContentLength;
        await using var src = await r.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);
        var buffer = new byte[81920];
        long done = 0; var last = DateTime.MinValue;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (DateTime.UtcNow - last > TimeSpan.FromMilliseconds(400))
            {
                last = DateTime.UtcNow;
                progress.Report(total is > 0 ? $"Downloading Ollama… {done * 100 / total:0}%" : $"Downloading Ollama… {done / 1048576} MB");
            }
        }
    }

    private static async Task<int> RunAsync(string file, string args, CancellationToken ct)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true })!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
