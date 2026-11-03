using System.Diagnostics;

namespace LifeHub.Desktop;

/// <summary>Runs LifeHub in Docker on this computer (for people who already use Docker).</summary>
public static class DockerSetup
{
    public const string Image = "ghcr.io/shotgn16/lifehub:latest";
    public const string Container = "lifehub";
    public const string Volume = "lifehub-data";

    public static string DownloadPage => OperatingSystem.IsLinux() ? "https://docs.docker.com/engine/install/" : "https://www.docker.com/products/docker-desktop/";

    public sealed record Status(bool Installed, bool Running, bool ContainerExists, string Detail);

    private static string Docker => OperatingSystem.IsMacOS() && !File.Exists("/usr/local/bin/docker") && File.Exists("/Applications/Docker.app/Contents/Resources/bin/docker")
        ? "/Applications/Docker.app/Contents/Resources/bin/docker" : "docker";

    private static async Task<(int Code, string Output)> RunAsync(string args, CancellationToken ct, IProgress<string>? progress = null)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Docker, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            var output = new System.Text.StringBuilder();
            void Line(string? l) { if (l is null) return; lock (output) output.AppendLine(l); progress?.Report(l); }
            p.OutputDataReceived += (_, e) => Line(e.Data);
            p.ErrorDataReceived += (_, e) => Line(e.Data);
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            await p.WaitForExitAsync(ct);
            return (p.ExitCode, output.ToString().Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    public static async Task<Status> CheckAsync(CancellationToken ct = default)
    {
        var (code, output) = await RunAsync("version --format {{.Server.Version}}", ct);
        if (code == -1) return new(false, false, false, "Docker isn't installed on this computer.");
        if (code != 0) return new(true, false, false, "Docker is installed but not running. Start Docker Desktop (or the Docker service), then press Check again.");
        var (c2, o2) = await RunAsync($"ps -a --filter name=^/{Container}$ --format {{{{.Status}}}}", ct);
        return new(true, true, c2 == 0 && o2.Length > 0, c2 == 0 && o2.Length > 0 ? "LifeHub container: " + o2 : "Docker " + output + " is ready.");
    }

    /// <summary>Downloads the LifeHub image and starts (or re-creates) the container. Your data stays in the "lifehub-data" volume.</summary>
    public static async Task RunContainerAsync(int port, IProgress<string> progress, CancellationToken ct)
    {
        progress.Report("Downloading LifeHub for Docker…");
        var (code, output) = await RunAsync("pull " + Image, ct, progress);
        if (code != 0) throw new InvalidOperationException("docker pull failed: " + Last(output));
        await RunAsync("rm -f " + Container, ct);
        progress.Report("Starting the container…");
        var tz = TimeZoneInfo.Local.HasIanaId ? TimeZoneInfo.Local.Id
               : TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : "UTC";
        (code, output) = await RunAsync($"run -d --name {Container} --restart unless-stopped -p {port}:8099 -v {Volume}:/data -e TZ={tz} {Image}", ct, progress);
        if (code != 0) throw new InvalidOperationException("docker run failed: " + Last(output));
        for (var i = 0; i < 40; i++)
        {
            if (await DesktopRuntime.IsLifeHubAsync($"http://localhost:{port}")) return;
            await Task.Delay(1500, ct);
        }
        throw new InvalidOperationException("The container started but LifeHub didn't answer. Check it with: docker logs lifehub");
    }

    public static Task RemoveContainerAsync(CancellationToken ct = default) => RunAsync("rm -f " + Container, ct);

    private static string Last(string s) => s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? s;
}
