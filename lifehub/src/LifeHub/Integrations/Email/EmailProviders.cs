using System.Reflection;
using System.Xml.Linq;

namespace LifeHub.Integrations.Email;

/// <summary>A mail service LifeHub knows how to set up by itself.</summary>
public sealed record MailService(
    string Key, string Name, string Icon, string ImapHost, int ImapPort,
    string AppPasswordUrl, string AppPasswordSteps, string[] Domains);

/// <summary>
/// The "add a mailbox" choices: Microsoft accounts sign in directly; the others connect with an app password
/// (a separate password the provider makes for apps like LifeHub). Anything else is found automatically from the
/// email address (Thunderbird's provider database, then common server names).
/// </summary>
public static class MailServices
{
    public static readonly MailService[] AppPassword =
    {
        new("gmail", "Gmail", "mail", "imap.gmail.com", 993, "https://myaccount.google.com/apppasswords",
            "Google needs 2-Step Verification switched on. Open the link, type “LifeHub” as the app name and press Create. Copy the 16-letter password it shows.",
            new[] { "gmail.com", "googlemail.com" }),
        new("icloud", "iCloud Mail", "mail", "imap.mail.me.com", 993, "https://account.apple.com/account/manage",
            "Sign in, open Sign-In and Security → App-Specific Passwords → +, name it “LifeHub” and copy the password.",
            new[] { "icloud.com", "me.com", "mac.com" }),
        new("yahoo", "Yahoo Mail", "mail", "imap.mail.yahoo.com", 993, "https://login.yahoo.com/account/security/app-passwords",
            "Sign in, choose Generate app password, name it “LifeHub” and copy the password.",
            new[] { "yahoo.com", "yahoo.co.uk", "ymail.com", "rocketmail.com" }),
        new("aol", "AOL Mail", "mail", "imap.aol.com", 993, "https://login.aol.com/account/security/app-passwords",
            "Sign in, choose Generate app password, name it “LifeHub” and copy the password.",
            new[] { "aol.com", "aol.co.uk" }),
        new("fastmail", "Fastmail", "mail", "imap.fastmail.com", 993, "https://app.fastmail.com/settings/security/apps",
            "Open the link, press New app password, choose IMAP access, and copy the password.",
            new[] { "fastmail.com", "fastmail.fm" }),
    };

    public static readonly string[] MicrosoftDomains = { "outlook.com", "hotmail.com", "hotmail.co.uk", "live.com", "live.co.uk", "msn.com" };

    public static MailService? ForAddress(string address)
    {
        var domain = DomainOf(address);
        return AppPassword.FirstOrDefault(s => s.Domains.Contains(domain));
    }

    public static string DomainOf(string address) => address.Contains('@') ? address[(address.LastIndexOf('@') + 1)..].Trim().ToLowerInvariant() : "";

    /// <summary>
    /// The Microsoft app (client) ID used by "Sign in with Microsoft": Settings → Email, else LIFEHUB_MS_CLIENT_ID,
    /// else the ID built into this copy of LifeHub (the <c>MicrosoftClientId</c> build property).
    /// </summary>
    public static string MicrosoftClientId(EmailSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.MicrosoftClientId) ? settings.MicrosoftClientId.Trim()
        : Environment.GetEnvironmentVariable("LIFEHUB_MS_CLIENT_ID") is { Length: > 0 } env ? env.Trim()
        : BuiltInMicrosoftClientId;

    public static string BuiltInMicrosoftClientId =>
        typeof(MailServices).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "MicrosoftClientId")?.Value?.Trim() ?? "";

    /// <summary>Finds the IMAP server for any address: Thunderbird's database (ISP's own autoconfig too), then guesses.</summary>
    public static async Task<(string Host, int Port, bool Ssl)?> DiscoverImapAsync(HttpClient http, string address, CancellationToken ct = default)
    {
        var domain = DomainOf(address);
        if (domain.Length == 0) return null;
        if (AppPassword.FirstOrDefault(s => s.Domains.Contains(domain)) is { } known) return (known.ImapHost, known.ImapPort, true);
        foreach (var url in new[]
                 {
                     $"https://autoconfig.thunderbird.net/v1.1/{domain}",
                     $"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(address)}",
                     $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml"
                 })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                var xml = XDocument.Parse(await http.GetStringAsync(url, cts.Token));
                var imap = xml.Descendants("incomingServer").FirstOrDefault(e => (string?)e.Attribute("type") == "imap");
                if (imap is null) continue;
                var host = ((string?)imap.Element("hostname") ?? "").Replace("%EMAILDOMAIN%", domain);
                var port = int.TryParse((string?)imap.Element("port"), out var p) ? p : 993;
                var ssl = ((string?)imap.Element("socketType") ?? "SSL").Equals("SSL", StringComparison.OrdinalIgnoreCase);
                if (host.Length > 0) return (host, port, ssl);
            }
            catch { }
        }
        return ($"imap.{domain}", 993, true);
    }
}
