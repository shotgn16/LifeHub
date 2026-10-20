namespace LifeHub.Integrations.Email;

public sealed class EmailMessage
{
    public string MessageId { get; set; } = "";
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime ReceivedUtc { get; set; }
    public string Body { get; set; } = "";
    /// <summary>Provider-specific handle used to find the message again (IMAP folder|uidvalidity|uid, Graph id, EWS ItemId).</summary>
    public string ProviderRef { get; set; } = "";
    /// <summary>Link to open the message in the mail provider's web app, when known.</summary>
    public string WebLink { get; set; } = "";
    /// <summary>Sent through a mailing list (List-Unsubscribe / bulk headers) — a strong hint it's marketing.</summary>
    public bool IsBulk { get; set; }
}

public enum EmailPresence { Present, Archived, Deleted, Unknown }

/// <summary>
/// One implementation per mail system. To add another (e.g. Gmail API, JMAP), implement this and
/// register it in Program.cs; the sync job picks providers by <see cref="Kind"/>.
/// </summary>
public interface IEmailProvider
{
    EmailAccountKind Kind { get; }

    /// <summary>
    /// Returns messages newer than the account's sync state, oldest first, and updates the
    /// sync fields on <paramref name="account"/> (the caller persists them).
    /// </summary>
    Task<IReadOnlyList<EmailMessage>> FetchNewAsync(EmailAccount account, EmailSettings settings, CancellationToken ct);

    /// <summary>Connects and returns a short status line, for the "Test" button.</summary>
    Task<string> TestAsync(EmailAccount account, CancellationToken ct);

    /// <summary>Moves the message to the mailbox's Bin / Deleted Items (never a permanent delete).</summary>
    Task MoveToTrashAsync(EmailAccount account, EmailRecord email, CancellationToken ct);

    /// <summary>
    /// Where the message is now: still in the inbox, archived (moved elsewhere), or deleted (in the bin or gone).
    /// </summary>
    Task<EmailPresence> GetPresenceAsync(EmailAccount account, EmailRecord email, CancellationToken ct);
}

public static class EmailText
{
    /// <summary>Very small HTML → text conversion for bodies that only come as HTML.</summary>
    public static string FromHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<(br|/p|/div|/tr|/li|/h\d)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[ \t ]+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\n\s*\n+", "\n\n");
        return s.Trim();
    }

    /// <summary>Trims quoted replies and signatures-ish noise and caps length for the model.</summary>
    public static string ForModel(string body, int maxChars)
    {
        var lines = body.Replace("\r", "").Split('\n');
        var kept = new List<string>();
        foreach (var line in lines)
        {
            var t = line.TrimStart();
            if (t.StartsWith(">")) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^(On .+ wrote:|-----Original Message-----|From: .+Sent: )")) break;
            kept.Add(line);
        }
        var text = string.Join('\n', kept).Trim();
        return text.Length > maxChars ? text[..maxChars] + "\n[…truncated]" : text;
    }
}
