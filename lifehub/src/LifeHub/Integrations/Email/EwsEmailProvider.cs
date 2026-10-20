using System.Net;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace LifeHub.Integrations.Email;

/// <summary>
/// On-premises Exchange (2013+) via Exchange Web Services, using NTLM/Negotiate/Basic with the
/// account's username and password. EWS URL is usually https://mail.yourdomain.com/EWS/Exchange.asmx.
/// (For Exchange Online use the Microsoft 365 account type — Microsoft has retired EWS there.)
/// </summary>
public sealed class EwsEmailProvider : IEmailProvider
{
    private static readonly XNamespace S = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace T = "http://schemas.microsoft.com/exchange/services/2006/types";
    private static readonly XNamespace M = "http://schemas.microsoft.com/exchange/services/2006/messages";

    public EmailAccountKind Kind => EmailAccountKind.ExchangeEws;

    private static HttpClient Client(EmailAccount a)
    {
        var user = string.IsNullOrEmpty(a.Username) ? a.Address : a.Username;
        var handler = new HttpClientHandler
        {
            Credentials = string.IsNullOrEmpty(a.EwsDomain) ? new NetworkCredential(user, a.Password) : new NetworkCredential(user, a.Password, a.EwsDomain),
            PreAuthenticate = true
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static string Envelope(string body) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:t="http://schemas.microsoft.com/exchange/services/2006/types" xmlns:m="http://schemas.microsoft.com/exchange/services/2006/messages">
          <soap:Header><t:RequestServerVersion Version="Exchange2013" /></soap:Header>
          <soap:Body>{body}</soap:Body>
        </soap:Envelope>
        """;

    private static async Task<XDocument> SendAsync(EmailAccount a, string body, CancellationToken ct)
    {
        using var client = Client(a);
        using var content = new StringContent(Envelope(body), Encoding.UTF8, "text/xml");
        using var r = await client.PostAsync(a.EwsUrl, content, ct);
        var text = await r.Content.ReadAsStringAsync(ct);
        if (r.StatusCode == HttpStatusCode.Unauthorized) throw new InvalidOperationException("EWS: authentication failed (check username/domain/password).");
        XDocument doc;
        try { doc = XDocument.Parse(text); }
        catch { throw new InvalidOperationException($"EWS returned {(int)r.StatusCode}: {text[..Math.Min(200, text.Length)]}"); }
        var fault = doc.Descendants(S + "Fault").FirstOrDefault();
        if (fault != null) throw new InvalidOperationException("EWS fault: " + fault.Element("faultstring")?.Value);
        var error = doc.Descendants().FirstOrDefault(e => e.Attribute("ResponseClass")?.Value == "Error");
        if (error != null) throw new InvalidOperationException($"EWS {error.Element(M + "ResponseCode")?.Value}: {error.Element(M + "MessageText")?.Value}");
        return doc;
    }

    public async Task<string> TestAsync(EmailAccount account, CancellationToken ct)
    {
        var doc = await SendAsync(account, """
            <m:GetFolder><m:FolderShape><t:BaseShape>Default</t:BaseShape></m:FolderShape>
            <m:FolderIds><t:DistinguishedFolderId Id="inbox"/></m:FolderIds></m:GetFolder>
            """, ct);
        var total = doc.Descendants(T + "TotalCount").FirstOrDefault()?.Value ?? "?";
        var unread = doc.Descendants(T + "UnreadCount").FirstOrDefault()?.Value ?? "?";
        return $"Connected. Inbox has {total} messages ({unread} unread).";
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchNewAsync(EmailAccount account, EmailSettings settings, CancellationToken ct)
    {
        var since = account.LastReceivedUtc ?? DateTime.UtcNow.AddDays(-settings.InitialLookbackDays);
        var find = await SendAsync(account, $"""
            <m:FindItem Traversal="Shallow">
              <m:ItemShape><t:BaseShape>IdOnly</t:BaseShape>
                <t:AdditionalProperties><t:FieldURI FieldURI="item:DateTimeReceived"/></t:AdditionalProperties></m:ItemShape>
              <m:IndexedPageItemView MaxEntriesReturned="{settings.MaxPerPoll}" Offset="0" BasePoint="Beginning"/>
              <m:Restriction><t:IsGreaterThan><t:FieldURI FieldURI="item:DateTimeReceived"/>
                <t:FieldURIOrConstant><t:Constant Value="{since:yyyy-MM-ddTHH:mm:ssZ}"/></t:FieldURIOrConstant></t:IsGreaterThan></m:Restriction>
              <m:SortOrder><t:FieldOrder Order="Ascending"><t:FieldURI FieldURI="item:DateTimeReceived"/></t:FieldOrder></m:SortOrder>
              <m:ParentFolderIds><t:DistinguishedFolderId Id="inbox"/></m:ParentFolderIds>
            </m:FindItem>
            """, ct);

        var ids = find.Descendants(T + "ItemId").Select(e => e.Attribute("Id")!.Value).ToList();
        if (ids.Count == 0)
        {
            account.LastReceivedUtc ??= DateTime.UtcNow;
            return Array.Empty<EmailMessage>();
        }

        var idXml = string.Join("", ids.Select(id => $"<t:ItemId Id=\"{SecurityElement.Escape(id)}\"/>"));
        var get = await SendAsync(account, $"""
            <m:GetItem>
              <m:ItemShape><t:BaseShape>Default</t:BaseShape><t:BodyType>Text</t:BodyType>
                <t:AdditionalProperties><t:FieldURI FieldURI="message:InternetMessageId"/><t:FieldURI FieldURI="item:DateTimeReceived"/></t:AdditionalProperties></m:ItemShape>
              <m:ItemIds>{idXml}</m:ItemIds>
            </m:GetItem>
            """, ct);

        var list = new List<EmailMessage>();
        foreach (var msg in get.Descendants(T + "Message"))
        {
            var received = DateTime.Parse(msg.Element(T + "DateTimeReceived")?.Value ?? DateTime.UtcNow.ToString("o"),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            var mailbox = msg.Element(T + "From")?.Element(T + "Mailbox");
            var itemId = msg.Element(T + "ItemId")?.Attribute("Id")?.Value ?? "";
            list.Add(new EmailMessage
            {
                ProviderRef = itemId,
                MessageId = msg.Element(T + "InternetMessageId")?.Value ?? (itemId.Length > 0 ? itemId : Guid.NewGuid().ToString()),
                Subject = msg.Element(T + "Subject")?.Value ?? "",
                From = mailbox is null ? "" : $"{mailbox.Element(T + "Name")?.Value} <{mailbox.Element(T + "EmailAddress")?.Value}>",
                ReceivedUtc = received,
                Body = msg.Element(T + "Body")?.Value ?? ""
            });
            if (received > (account.LastReceivedUtc ?? DateTime.MinValue)) account.LastReceivedUtc = received;
        }
        return list.OrderBy(m => m.ReceivedUtc).ToList();
    }

    private static bool IsNotFound(Exception ex) =>
        ex.Message.Contains("ErrorItemNotFound") || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>Exchange item IDs change when a message moves folder, so we fall back to the Internet Message-ID.</summary>
    private static async Task<string?> FindByMessageIdAsync(EmailAccount a, string folder, string messageId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(messageId)) return null;
        var doc = await SendAsync(a, $"""
            <m:FindItem Traversal="Shallow">
              <m:ItemShape><t:BaseShape>IdOnly</t:BaseShape></m:ItemShape>
              <m:IndexedPageItemView MaxEntriesReturned="1" Offset="0" BasePoint="Beginning"/>
              <m:Restriction><t:IsEqualTo><t:FieldURI FieldURI="message:InternetMessageId"/>
                <t:FieldURIOrConstant><t:Constant Value="{SecurityElement.Escape(messageId)}"/></t:FieldURIOrConstant></t:IsEqualTo></m:Restriction>
              <m:ParentFolderIds><t:DistinguishedFolderId Id="{folder}"/></m:ParentFolderIds>
            </m:FindItem>
            """, ct);
        return doc.Descendants(T + "ItemId").FirstOrDefault()?.Attribute("Id")?.Value;
    }

    public async Task MoveToTrashAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        var id = email.ProviderRef;
        if (string.IsNullOrEmpty(id) || !await ExistsAsync(account, id, ct))
            id = await FindByMessageIdAsync(account, "inbox", email.MessageId, ct) ?? "";
        if (string.IsNullOrEmpty(id)) return; // not in the inbox any more — nothing to move
        await SendAsync(account, $"""
            <m:MoveItem><m:ToFolderId><t:DistinguishedFolderId Id="deleteditems"/></m:ToFolderId>
            <m:ItemIds><t:ItemId Id="{SecurityElement.Escape(id)}"/></m:ItemIds></m:MoveItem>
            """, ct);
    }

    private static async Task<bool> ExistsAsync(EmailAccount a, string itemId, CancellationToken ct)
    {
        try
        {
            await SendAsync(a, $"""
                <m:GetItem><m:ItemShape><t:BaseShape>IdOnly</t:BaseShape></m:ItemShape>
                <m:ItemIds><t:ItemId Id="{SecurityElement.Escape(itemId)}"/></m:ItemIds></m:GetItem>
                """, ct);
            return true;
        }
        catch (InvalidOperationException ex) when (IsNotFound(ex)) { return false; }
    }

    public async Task<EmailPresence> GetPresenceAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        if (await FindByMessageIdAsync(account, "inbox", email.MessageId, ct) is not null) return EmailPresence.Present;
        if (await FindByMessageIdAsync(account, "deleteditems", email.MessageId, ct) is not null) return EmailPresence.Deleted;
        // Not in the inbox or the bin: archived/moved elsewhere (or emptied from the bin). Leave tasks alone.
        return EmailPresence.Archived;
    }
}
