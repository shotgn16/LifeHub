using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace LifeHub.Integrations.Email;

/// <summary>
/// IMAP via MailKit. Works with Gmail (enable 2FA and use an App Password, imap.gmail.com:993),
/// Outlook.com/iCloud/Fastmail with app passwords, and on-prem Exchange where IMAP is enabled.
/// Reading opens the folder read-only with BODY.PEEK, so messages are never marked as read.
/// The only change LifeHub ever makes is moving a message to the Bin when you delete its task.
/// </summary>
public sealed class ImapEmailProvider : IEmailProvider
{
    public EmailAccountKind Kind => EmailAccountKind.Imap;

    private static readonly string[] TrashNames = { "[Gmail]/Trash", "[Gmail]/Bin", "[Google Mail]/Trash", "[Google Mail]/Bin", "Trash", "Bin", "Deleted Items", "Deleted Messages", "INBOX.Trash", "INBOX/Trash" };

    private static async Task<ImapClient> ConnectAsync(EmailAccount a, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 30_000 };
        var options = a.ImapSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
        await client.ConnectAsync(a.ImapHost, a.ImapPort, options, ct);
        await client.AuthenticateAsync(string.IsNullOrEmpty(a.Username) ? a.Address : a.Username, a.Password, ct);
        return client;
    }

    private static async Task<IMailFolder> GetFolderAsync(ImapClient client, string name, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(name) || name.Equals("INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(name, ct);

    private static async Task<IMailFolder?> FindTrashAsync(ImapClient client, CancellationToken ct)
    {
        if ((client.Capabilities & ImapCapabilities.SpecialUse) != 0)
        {
            try { var special = client.GetFolder(SpecialFolder.Trash); if (special is not null) return special; } catch { }
        }
        foreach (var name in TrashNames)
        {
            try { return await client.GetFolderAsync(name, ct); } catch { /* try next */ }
        }
        return null;
    }

    public async Task<string> TestAsync(EmailAccount account, CancellationToken ct)
    {
        using var client = await ConnectAsync(account, ct);
        var folder = await GetFolderAsync(client, account.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var count = folder.Count;
        var trash = await FindTrashAsync(client, ct);
        await client.DisconnectAsync(true, ct);
        return $"Connected. {folder.FullName} has {count} messages. Bin folder: {trash?.FullName ?? "not found (deleting emails from LifeHub won't work)"}.";
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchNewAsync(EmailAccount account, EmailSettings settings, CancellationToken ct)
    {
        using var client = await ConnectAsync(account, ct);
        var folder = await GetFolderAsync(client, account.Folder, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);

        IList<UniqueId> uids;
        if (account.LastImapUid == 0 || account.ImapUidValidity != folder.UidValidity)
        {
            // First sync (or the server reset UIDs): only look back a little.
            account.LastImapUid = 0;
            account.ImapUidValidity = folder.UidValidity;
            uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(DateTime.UtcNow.AddDays(-settings.InitialLookbackDays)), ct);
        }
        else
        {
            var range = new UniqueIdRange(new UniqueId(account.LastImapUid + 1), UniqueId.MaxValue);
            uids = await folder.SearchAsync(range, SearchQuery.All, ct);
        }

        var batch = uids.Where(u => u.Id > account.LastImapUid).OrderBy(u => u.Id).Take(settings.MaxPerPoll).ToList();
        var result = new List<EmailMessage>();
        var gmail = account.ImapHost.Contains("gmail", StringComparison.OrdinalIgnoreCase) || account.ImapHost.Contains("googlemail", StringComparison.OrdinalIgnoreCase);
        foreach (var uid in batch)
        {
            var msg = await folder.GetMessageAsync(uid, ct);
            var body = msg.TextBody;
            if (string.IsNullOrWhiteSpace(body)) body = EmailText.FromHtml(msg.HtmlBody ?? "");
            var messageId = msg.MessageId ?? "";
            result.Add(new EmailMessage
            {
                MessageId = string.IsNullOrEmpty(messageId) ? $"imap:{account.Id}:{folder.UidValidity}:{uid.Id}" : messageId,
                From = msg.From?.ToString() ?? "",
                Subject = msg.Subject ?? "",
                ReceivedUtc = msg.Date.UtcDateTime,
                Body = body ?? "",
                ProviderRef = $"{folder.FullName}|{folder.UidValidity}|{uid.Id}",
                WebLink = gmail && messageId.Length > 0 ? $"https://mail.google.com/mail/#search/rfc822msgid%3A{Uri.EscapeDataString(messageId)}" : "",
                IsBulk = msg.Headers.Contains("List-Unsubscribe") ||
                         (msg.Headers["Precedence"] ?? "").Contains("bulk", StringComparison.OrdinalIgnoreCase)
            });
            account.LastImapUid = uid.Id;
        }
        // Nothing recent on first sync: start from "now" so the next poll only sees new mail.
        if (account.LastImapUid == 0 && folder.UidNext is { } next && next.Id > 0)
            account.LastImapUid = next.Id - 1;
        await client.DisconnectAsync(true, ct);
        return result;
    }

    /// <summary>Parses "folder|uidvalidity|uid".</summary>
    private static (string Folder, uint Validity, uint Uid)? ParseRef(string providerRef)
    {
        var p = providerRef.Split('|');
        return p.Length == 3 && uint.TryParse(p[1], out var v) && uint.TryParse(p[2], out var u) ? (p[0], v, u) : null;
    }

    /// <summary>Finds the message's current UID in a folder: by UID when still valid, else by Message-ID.</summary>
    private static async Task<UniqueId?> LocateAsync(IMailFolder folder, EmailRecord email, CancellationToken ct)
    {
        var r = ParseRef(email.ProviderRef);
        if (r is { } x && x.Folder == folder.FullName && x.Validity == folder.UidValidity)
        {
            var hit = await folder.SearchAsync(new[] { new UniqueId(x.Uid) }, SearchQuery.All, ct);
            return hit.Count > 0 ? hit[0] : null;
        }
        if (string.IsNullOrEmpty(email.MessageId) || email.MessageId.StartsWith("imap:")) return null;
        var found = await folder.SearchAsync(SearchQuery.HeaderContains("Message-Id", email.MessageId), ct);
        return found.Count > 0 ? found[0] : null;
    }

    public async Task MoveToTrashAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        using var client = await ConnectAsync(account, ct);
        var trash = await FindTrashAsync(client, ct) ?? throw new InvalidOperationException("Couldn't find a Bin/Trash folder on this mailbox.");
        var folderName = ParseRef(email.ProviderRef)?.Folder ?? account.Folder;
        var folder = await GetFolderAsync(client, folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadWrite, ct);
        var uid = await LocateAsync(folder, email, ct);
        if (uid is { } u) await folder.MoveToAsync(u, trash, ct);
        await client.DisconnectAsync(true, ct);
    }

    public async Task<EmailPresence> GetPresenceAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        using var client = await ConnectAsync(account, ct);
        var folderName = ParseRef(email.ProviderRef)?.Folder ?? account.Folder;
        var folder = await GetFolderAsync(client, folderName, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var presence = EmailPresence.Unknown;
        if (await LocateAsync(folder, email, ct) is not null) presence = EmailPresence.Present;
        else
        {
            // Gone from the inbox: in the bin means deleted, anywhere else (e.g. archived) we leave alone.
            var trash = await FindTrashAsync(client, ct);
            if (trash is not null && !string.IsNullOrEmpty(email.MessageId) && !email.MessageId.StartsWith("imap:"))
            {
                await trash.OpenAsync(FolderAccess.ReadOnly, ct);
                var inTrash = await trash.SearchAsync(SearchQuery.HeaderContains("Message-Id", email.MessageId), ct);
                presence = inTrash.Count > 0 ? EmailPresence.Deleted : EmailPresence.Archived;
            }
            else presence = EmailPresence.Archived;
        }
        await client.DisconnectAsync(true, ct);
        return presence;
    }
}
