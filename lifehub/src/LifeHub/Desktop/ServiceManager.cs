using System.Diagnostics;
using System.Text;

namespace LifeHub.Desktop;

/// <summary>
/// Runs LifeHub in the background so phones and other computers can reach it even with the app closed:
/// <list type="bullet">
/// <item>Windows: a scheduled task at sign-in ("user"), or a Windows service ("system", needs administrator).</item>
/// <item>macOS: a LaunchAgent ("user"), or a LaunchDaemon ("system", asks for your password).</item>
/// <item>Linux: a systemd user service ("user"), kept running after sign-out with lingering ("system", asks for your password).</item>
/// </list>
/// The background copy uses the same data folder as the app.
/// </summary>
public static class ServiceManager
{
    public const string Name = "LifeHub";
    private const string MacLabel = "io.github.shotgn16.lifehub";

    public sealed record Result(bool Ok, string Message);

    private static (int Code, string Output) Run(string file, string args, bool wait = true)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            if (!wait) return (0, "");
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            return (p.ExitCode, output.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

    public static bool IsInstalled()
    {
        if (OperatingSystem.IsWindows())
            return Run("sc.exe", "query " + Name).Code == 0 || Run("schtasks.exe", $"/Query /TN {Name}").Code == 0;
        if (OperatingSystem.IsMacOS())
            return File.Exists(MacAgentPath) || File.Exists($"/Library/LaunchDaemons/{MacLabel}.plist");
        return File.Exists(LinuxUnitPath);
    }

    /// <summary>Installs and starts the background service.</summary>
    public static Result Install(string scope, string dataDir, int port)
    {
        var exe = DesktopPaths.Executable;
        var args = $"--server --data {Quote(dataDir)} --port {port}";
        try
        {
            if (OperatingSystem.IsWindows()) return scope == "system" ? WindowsServiceElevated(true, dataDir, port) : WindowsTask(exe, args);
            if (OperatingSystem.IsMacOS()) return scope == "system" ? MacDaemon(exe, dataDir, port) : MacAgent(exe, dataDir, port);
            return LinuxUser(exe, dataDir, port, linger: scope == "system");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    public static Result Uninstall()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Run("schtasks.exe", $"/End /TN {Name}");
                Run("schtasks.exe", $"/Delete /TN {Name} /F");
                if (Run("sc.exe", "query " + Name).Code == 0) return WindowsServiceElevated(false, "", 0);
                return new(true, "Removed");
            }
            if (OperatingSystem.IsMacOS())
            {
                if (File.Exists(MacAgentPath))
                {
                    Run("launchctl", $"bootout gui/{GetUid()} {Quote(MacAgentPath)}");
                    File.Delete(MacAgentPath);
                }
                var daemon = $"/Library/LaunchDaemons/{MacLabel}.plist";
                if (File.Exists(daemon)) MacAdmin($"launchctl bootout system {daemon}; rm -f {daemon}");
                return new(true, "Removed");
            }
            Run("systemctl", "--user disable --now lifehub.service");
            if (File.Exists(LinuxUnitPath)) File.Delete(LinuxUnitPath);
            Run("systemctl", "--user daemon-reload");
            return new(true, "Removed");
        }
        catch (Exception ex) { return new(false, ex.Message); }
    }

    // ---------------------------------------------------------------- Windows

    private static Result WindowsTask(string exe, string args)
    {
        var (code, output) = Run("schtasks.exe", $"/Create /TN {Name} /TR {Quote($"\"{exe}\" {args}")} /SC ONLOGON /RL LIMITED /F");
        if (code != 0) return new(false, "Couldn't create the scheduled task: " + output);
        Run("schtasks.exe", $"/Run /TN {Name}");
        return new(true, "LifeHub now runs in the background whenever you sign in to Windows.");
    }

    /// <summary>Re-runs LifeHub as administrator (Windows asks first) to add or remove the Windows service.</summary>
    private static Result WindowsServiceElevated(bool install, string dataDir, int port)
    {
        var args = install ? $"--install-system-service --data {Quote(dataDir)} --port {port}" : "--remove-system-service";
        try
        {
            using var p = Process.Start(new ProcessStartInfo(DesktopPaths.Executable, args) { UseShellExecute = true, Verb = "runas" })!;
            p.WaitForExit(120_000);
            return p.ExitCode == 0
                ? new(true, install ? "LifeHub is installed as a Windows service and starts with the computer." : "Removed")
                : new(false, "The Windows service couldn't be set up (code " + p.ExitCode + ").");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new(false, "Administrator permission was declined. You can choose “when I sign in” instead.");
        }
    }

    /// <summary>Runs inside the elevated copy: creates the service and opens the firewall port.</summary>
    public static int WindowsServiceAdmin(bool install, string dataDir, int port)
    {
        if (install)
        {
            var bin = $"\"{DesktopPaths.Executable}\" --server --windows-service --data \"{dataDir}\" --port {port}";
            Run("sc.exe", $"stop {Name}");
            Run("sc.exe", $"delete {Name}");
            var (code, _) = Run("sc.exe", $"create {Name} binPath= {Quote(bin)} start= auto DisplayName= \"LifeHub\"");
            if (code != 0) return code;
            Run("sc.exe", $"description {Name} \"LifeHub household organiser (http://localhost:{port})\"");
            Run("netsh.exe", $"advfirewall firewall delete rule name=\"LifeHub\"");
            Run("netsh.exe", $"advfirewall firewall add rule name=\"LifeHub\" dir=in action=allow protocol=TCP localport={port}");
            return Run("sc.exe", $"start {Name}").Code is 0 or 1056 ? 0 : 1;
        }
        Run("sc.exe", $"stop {Name}");
        Run("netsh.exe", "advfirewall firewall delete rule name=\"LifeHub\"");
        return Run("sc.exe", $"delete {Name}").Code;
    }

    // ---------------------------------------------------------------- macOS

    private static string MacAgentPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", MacLabel + ".plist");

    private static string GetUid() => Run("id", "-u").Output.Trim();

    private static string Plist(string exe, string dataDir, int port, string? user = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\"><dict>");
        sb.AppendLine($"<key>Label</key><string>{MacLabel}</string>");
        sb.AppendLine("<key>ProgramArguments</key><array>");
        foreach (var a in new[] { exe, "--server", "--data", dataDir, "--port", port.ToString() })
            sb.AppendLine($"<string>{System.Security.SecurityElement.Escape(a)}</string>");
        sb.AppendLine("</array>");
        if (user is not null) sb.AppendLine($"<key>UserName</key><string>{user}</string>");
        sb.AppendLine("<key>RunAtLoad</key><true/><key>KeepAlive</key><true/>");
        sb.AppendLine($"<key>StandardOutPath</key><string>{System.Security.SecurityElement.Escape(Path.Combine(dataDir, "lifehub.log"))}</string>");
        sb.AppendLine($"<key>StandardErrorPath</key><string>{System.Security.SecurityElement.Escape(Path.Combine(dataDir, "lifehub.log"))}</string>");
        sb.AppendLine("</dict></plist>");
        return sb.ToString();
    }

    private static Result MacAgent(string exe, string dataDir, int port)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MacAgentPath)!);
        Run("launchctl", $"bootout gui/{GetUid()} {Quote(MacAgentPath)}");
        File.WriteAllText(MacAgentPath, Plist(exe, dataDir, port));
        var (code, output) = Run("launchctl", $"bootstrap gui/{GetUid()} {Quote(MacAgentPath)}");
        return code == 0 ? new(true, "LifeHub now runs in the background whenever you're signed in to this Mac.")
                         : new(false, "launchctl: " + output);
    }

    private static (int Code, string Output) MacAdmin(string shell) =>
        Run("osascript", "-e " + Quote($"do shell script \"{shell.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" with administrator privileges"));

    private static Result MacDaemon(string exe, string dataDir, int port)
    {
        var tmp = Path.Combine(Path.GetTempPath(), MacLabel + ".plist");
        File.WriteAllText(tmp, Plist(exe, dataDir, port, Environment.UserName));
        var target = $"/Library/LaunchDaemons/{MacLabel}.plist";
        var (code, output) = MacAdmin($"launchctl bootout system {target} 2>/dev/null; cp '{tmp}' {target} && chown root:wheel {target} && chmod 644 {target} && launchctl bootstrap system {target}");
        return code == 0 ? new(true, "LifeHub now starts with this Mac, even before anyone signs in.")
                         : new(false, output.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "Cancelled. You can choose “when I sign in” instead." : output);
    }

    // ---------------------------------------------------------------- Linux

    private static string LinuxUnitPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "systemd", "user", "lifehub.service");

    private static Result LinuxUser(string exe, string dataDir, int port, bool linger)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LinuxUnitPath)!);
        File.WriteAllText(LinuxUnitPath, $"""
            [Unit]
            Description=LifeHub household organiser
            After=network-online.target

            [Service]
            ExecStart="{exe}" --server --data "{dataDir}" --port {port}
            Restart=on-failure
            RestartSec=5

            [Install]
            WantedBy=default.target
            """);
        Run("systemctl", "--user daemon-reload");
        var (code, output) = Run("systemctl", "--user enable --now lifehub.service");
        if (code != 0) return new(false, "systemd: " + output);
        if (linger)
        {
            var user = Environment.UserName;
            var (lc, lo) = Run("pkexec", $"loginctl enable-linger {user}");
            if (lc != 0) return new(true, "LifeHub runs in the background while you're signed in. (Starting before sign-in needs: sudo loginctl enable-linger " + user + ")");
            return new(true, "LifeHub now starts with this computer, even before anyone signs in.");
        }
        return new(true, "LifeHub now runs in the background whenever you're signed in.");
    }
}
