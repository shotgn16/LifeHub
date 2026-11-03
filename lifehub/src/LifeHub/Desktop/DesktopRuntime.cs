using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace LifeHub.Desktop;

/// <summary>
/// What the LifeHub desktop app tells the web app about this computer. In server builds (Docker, Home Assistant)
/// <see cref="Active"/> is false and the setup assistant isn't offered.
/// </summary>
public static class DesktopRuntime
{
    /// <summary>True in the desktop app and in its background service.</summary>
    public static bool Active { get; set; }
    /// <summary>True in the copy running as a background service (no window).</summary>
    public static bool IsService { get; set; }
    public static string DataDir { get; set; } = "";
    public static string Os => OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

    public static HostConfig Config() => HostConfig.Load(DataDir);
    public static void Save(HostConfig c) => c.Save(DataDir);

    /// <summary>The secret the app window uses to sign the main person in without a password (only from this computer).</summary>
    public static string TokenFile => Path.Combine(DataDir, "keys", "desktop.token");

    public static string EnsureToken()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
        if (File.Exists(TokenFile))
        {
            var t = File.ReadAllText(TokenFile).Trim();
            if (t.Length >= 32) return t;
        }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        File.WriteAllText(TokenFile, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(TokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return token;
    }

    public static bool CheckToken(string? token)
    {
        if (!Active || string.IsNullOrEmpty(token) || !File.Exists(TokenFile)) return false;
        var expected = System.Text.Encoding.UTF8.GetBytes(File.ReadAllText(TokenFile).Trim());
        return CryptographicOperations.FixedTimeEquals(expected, System.Text.Encoding.UTF8.GetBytes(token.Trim()));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Guid User, DateTime Expires)> _once = new();

    /// <summary>A one-time sign-in code (used right after the setup assistant creates the account).</summary>
    public static string IssueOnce(Guid user)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        _once[code] = (user, DateTime.UtcNow.AddMinutes(2));
        return code;
    }

    public static Guid? TakeOnce(string? code)
    {
        if (string.IsNullOrEmpty(code) || !_once.TryRemove(code, out var e)) return null;
        return e.Expires > DateTime.UtcNow ? e.User : null;
    }

    /// <summary>Opens a web page in the computer's normal browser.</summary>
    public static void OpenInBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            else Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", url);
        }
        catch { }
    }

    /// <summary>The addresses other devices on the network can use to reach this computer.</summary>
    public static List<string> LanAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var name = ni.Name.ToLowerInvariant();
                if (name.StartsWith("docker") || name.StartsWith("veth") || name.StartsWith("br-") || name.Contains("vethernet") || name.StartsWith("utun")) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254."))
                        list.Add(a.Address.ToString());
            }
        }
        catch { }
        return list.Distinct().OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2).ToList();
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static bool PortFree(int port)
    {
        try { var l = new TcpListener(IPAddress.Any, port); l.Start(); l.Stop(); return true; }
        catch { return false; }
    }

    /// <summary>Whether a LifeHub answers at this address.</summary>
    public static async Task<bool> IsLifeHubAsync(string baseUrl, int timeoutMs = 2500)
    {
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            var body = await c.GetStringAsync(baseUrl.TrimEnd('/') + "/health");
            return body.Contains("\"status\"") && body.Contains("ok");
        }
        catch { return false; }
    }

    /// <summary>Total memory of this computer in GB (to pick an AI model that fits).</summary>
    public static double MemoryGb()
    {
        try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024d / 1024d / 1024d; }
        catch { return 8; }
    }
}
