using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace LifeHub.Integrations.Email;

public sealed record DeviceCodeInfo(string DeviceCode, string UserCode, string VerificationUri, string Message, int Interval, DateTime ExpiresUtc);

/// <summary>
/// Microsoft 365 / Outlook.com / Exchange Online via Microsoft Graph, signed in with the OAuth
/// device-code flow (no redirect URL needed, works behind ingress). Requires an Entra ID app
/// registration with "Allow public client flows" enabled — see README.
/// </summary>
public sealed class MicrosoftGraphClient
{
    // Mail.ReadWrite is only used to move a message to Deleted Items when you delete its task.
    public const string Scopes = "offline_access User.Read Mail.ReadWrite Calendars.ReadWrite";
    private readonly IHttpClientFactory _http;
    private readonly SettingsService _settings;
    private readonly ILogger<MicrosoftGraphClient> _log;
    private readonly ConcurrentDictionary<Guid, (string Token, DateTime ExpiresUtc)> _tokens = new();

    /// <summary>Status of in-progress sign-ins, shown on the Settings page.</summary>
    public ConcurrentDictionary<Guid, string> LoginStatus { get; } = new();

    public MicrosoftGraphClient(IHttpClientFactory http, SettingsService settings, ILogger<MicrosoftGraphClient> log)
    {
        _http = http; _settings = settings; _log = log;
    }

    private static string Authority(EmailAccount a) =>
        $"https://login.microsoftonline.com/{(string.IsNullOrWhiteSpace(a.GraphTenant) ? "common" : a.GraphTenant)}/oauth2/v2.0";

    public async Task<DeviceCodeInfo> StartDeviceLoginAsync(EmailAccount a, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(a.GraphClientId)) throw new InvalidOperationException("Enter the Application (client) ID first.");
        using var r = await _http.CreateClient().PostAsync(Authority(a) + "/devicecode",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = a.GraphClientId, ["scope"] = Scopes }), ct);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException(ErrorText(j));
        return new DeviceCodeInfo(j.GetProperty("device_code").GetString()!, j.GetProperty("user_code").GetString()!,
            j.GetProperty("verification_uri").GetString()!, j.GetProperty("message").GetString() ?? "",
            j.TryGetProperty("interval", out var i) ? i.GetInt32() : 5, DateTime.UtcNow.AddSeconds(j.GetProperty("expires_in").GetInt32()));
    }

    /// <summary>Polls until the user finishes signing in, then stores the refresh token.</summary>
    public async Task CompleteDeviceLoginAsync(Guid accountId, DeviceCodeInfo info, CancellationToken ct = default)
    {
        var account = _settings.Current.Email.Accounts.First(x => x.Id == accountId);
        var interval = Math.Max(2, info.Interval);
        LoginStatus[accountId] = $"Waiting — go to {info.VerificationUri} and enter {info.UserCode}";
        while (DateTime.UtcNow < info.ExpiresUtc && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            using var r = await _http.CreateClient().PostAsync(Authority(account) + "/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = account.GraphClientId,
                ["device_code"] = info.DeviceCode
            }), ct);
            var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (r.IsSuccessStatusCode)
            {
                await StoreTokensAsync(accountId, j);
                var me = await GetJsonAsync(accountId, "me?$select=mail,userPrincipalName,displayName", ct);
                var address = me.TryGetProperty("mail", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : me.GetProperty("userPrincipalName").GetString();
                await _settings.UpdateAsync(s =>
                {
                    var acc = s.Email.Accounts.First(x => x.Id == accountId);
                    acc.Address = address ?? acc.Address;
                    acc.LastError = "";
                });
                LoginStatus[accountId] = $"Signed in as {address}";
                return;
            }
            var err = j.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
            if (err == "authorization_pending") continue;
            if (err == "slow_down") { interval += 5; continue; }
            LoginStatus[accountId] = "Sign-in failed: " + ErrorText(j);
            return;
        }
        LoginStatus[accountId] = "Sign-in code expired — try again.";
    }

    private async Task StoreTokensAsync(Guid accountId, JsonElement j)
    {
        var access = j.GetProperty("access_token").GetString()!;
        var expires = DateTime.UtcNow.AddSeconds(j.GetProperty("expires_in").GetInt32() - 120);
        _tokens[accountId] = (access, expires);
        if (j.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { Length: > 0 } refresh)
            await _settings.UpdateAsync(s => s.Email.Accounts.First(x => x.Id == accountId).GraphRefreshToken = refresh);
    }

    public async Task<string> GetAccessTokenAsync(Guid accountId, CancellationToken ct = default)
    {
        if (_tokens.TryGetValue(accountId, out var t) && t.ExpiresUtc > DateTime.UtcNow) return t.Token;
        var a = _settings.Current.Email.Accounts.FirstOrDefault(x => x.Id == accountId)
                ?? throw new InvalidOperationException("Account not found");
        if (string.IsNullOrEmpty(a.GraphRefreshToken)) throw new InvalidOperationException("Not signed in — use Sign in on the Settings page.");
        using var r = await _http.CreateClient().PostAsync(Authority(a) + "/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = a.GraphClientId,
            ["refresh_token"] = a.GraphRefreshToken,
            ["scope"] = Scopes
        }), ct);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Token refresh failed (sign in again): " + ErrorText(j));
        await StoreTokensAsync(accountId, j);
        return _tokens[accountId].Token;
    }

    private async Task<HttpClient> ClientAsync(Guid accountId, CancellationToken ct)
    {
        var c = _http.CreateClient();
        c.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetAccessTokenAsync(accountId, ct));
        c.DefaultRequestHeaders.Add("Prefer", "outlook.body-content-type=\"text\"");
        c.DefaultRequestHeaders.Add("Prefer", "outlook.timezone=\"UTC\"");
        // Immutable IDs stay the same when a message is moved (archived, deleted…), so we can track it.
        c.DefaultRequestHeaders.Add("Prefer", "IdType=\"ImmutableId\"");
        return c;
    }

    public async Task<JsonElement> GetJsonAsync(Guid accountId, string path, CancellationToken ct = default)
    {
        using var c = await ClientAsync(accountId, ct);
        using var r = await c.GetAsync(path, ct);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Graph: " + ErrorText(j));
        return j;
    }

    /// <summary>GET that returns null on 404 (message gone).</summary>
    public async Task<JsonElement?> TryGetJsonAsync(Guid accountId, string path, CancellationToken ct = default)
    {
        using var c = await ClientAsync(accountId, ct);
        using var r = await c.GetAsync(path, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Graph: " + ErrorText(j));
        return j;
    }

    public async Task<JsonElement> PostJsonAsync(Guid accountId, string path, object body, CancellationToken ct = default)
    {
        using var c = await ClientAsync(accountId, ct);
        using var r = await c.PostAsync(path, JsonHttp.Body(body), ct);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException("Graph: " + ErrorText(j));
        return j;
    }

    public async Task<List<CalendarEvent>> GetEventsAsync(EmailAccount a, DateTime startLocal, DateTime endLocal, Clock clock, CancellationToken ct = default)
    {
        var s = clock.ToUtc(startLocal).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var e = clock.ToUtc(endLocal).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var j = await GetJsonAsync(a.Id, $"me/calendarView?startDateTime={s}&endDateTime={e}&$top=200&$select=subject,start,end,isAllDay,location,bodyPreview&$orderby=start/dateTime", ct);
        var list = new List<CalendarEvent>();
        foreach (var ev in j.GetProperty("value").EnumerateArray())
        {
            var allDay = ev.GetProperty("isAllDay").GetBoolean();
            DateTime Parse(string prop)
            {
                var dt = DateTime.Parse(ev.GetProperty(prop).GetProperty("dateTime").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                return allDay ? dt.Date : clock.ToLocal(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
            }
            list.Add(new CalendarEvent
            {
                Title = ev.GetProperty("subject").GetString() ?? "",
                Start = Parse("start"), End = Parse("end"), AllDay = allDay,
                Location = ev.TryGetProperty("location", out var l) && l.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "",
                Description = ev.TryGetProperty("bodyPreview", out var bp) ? bp.GetString() ?? "" : "",
                Source = a.Name
            });
        }
        return list;
    }

    public async Task<string> CreateEventAsync(EmailAccount a, string title, DateTime startLocal, DateTime endLocal, string description, bool allDay, Clock clock, CancellationToken ct = default)
    {
        object start, end;
        if (allDay)
        {
            var tz = TimeZoneInfo.TryConvertIanaIdToWindowsId(clock.Zone.Id, out var win) ? win : clock.Zone.Id;
            var endDate = endLocal.Date <= startLocal.Date ? startLocal.Date.AddDays(1) : endLocal.Date;
            start = new { dateTime = startLocal.Date.ToString("yyyy-MM-ddT00:00:00"), timeZone = tz };
            end = new { dateTime = endDate.ToString("yyyy-MM-ddT00:00:00"), timeZone = tz };
        }
        else
        {
            start = new { dateTime = clock.ToUtc(startLocal).ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" };
            end = new { dateTime = clock.ToUtc(endLocal).ToString("yyyy-MM-ddTHH:mm:ss"), timeZone = "UTC" };
        }
        var j = await PostJsonAsync(a.Id, "me/events", new
        {
            subject = title,
            body = new { contentType = "text", content = description },
            start, end, isAllDay = allDay
        }, ct);
        return j.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
    }

    private static string ErrorText(JsonElement j)
    {
        if (j.ValueKind != JsonValueKind.Object) return "unknown error";
        if (j.TryGetProperty("error_description", out var d)) return d.GetString()?.Split('\n')[0] ?? "";
        if (j.TryGetProperty("error", out var e))
            return e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m) ? m.GetString() ?? "" : e.ToString();
        return j.ToString();
    }
}

public sealed class GraphEmailProvider : IEmailProvider
{
    private readonly MicrosoftGraphClient _graph;
    public GraphEmailProvider(MicrosoftGraphClient graph) => _graph = graph;

    public EmailAccountKind Kind => EmailAccountKind.MicrosoftGraph;

    public async Task<string> TestAsync(EmailAccount account, CancellationToken ct)
    {
        var j = await _graph.GetJsonAsync(account.Id, "me/mailFolders/inbox?$select=displayName,totalItemCount,unreadItemCount", ct);
        return $"Connected. Inbox has {j.GetProperty("totalItemCount").GetInt32()} messages ({j.GetProperty("unreadItemCount").GetInt32()} unread).";
    }

    public async Task<IReadOnlyList<EmailMessage>> FetchNewAsync(EmailAccount account, EmailSettings settings, CancellationToken ct)
    {
        var since = account.LastReceivedUtc ?? DateTime.UtcNow.AddDays(-settings.InitialLookbackDays);
        var filter = Uri.EscapeDataString($"receivedDateTime gt {since:yyyy-MM-ddTHH:mm:ssZ}");
        var j = await _graph.GetJsonAsync(account.Id,
            $"me/mailFolders/inbox/messages?$filter={filter}&$orderby=receivedDateTime asc&$top={settings.MaxPerPoll}" +
            "&$select=id,internetMessageId,subject,from,receivedDateTime,body,webLink,internetMessageHeaders", ct);
        var list = new List<EmailMessage>();
        foreach (var m in j.GetProperty("value").EnumerateArray())
        {
            var received = m.GetProperty("receivedDateTime").GetDateTime().ToUniversalTime();
            var from = m.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Object
                ? $"{f.GetProperty("emailAddress").GetProperty("name").GetString()} <{f.GetProperty("emailAddress").GetProperty("address").GetString()}>"
                : "";
            var body = m.GetProperty("body");
            var content = body.GetProperty("content").GetString() ?? "";
            if (body.GetProperty("contentType").GetString() == "html") content = EmailText.FromHtml(content);
            list.Add(new EmailMessage
            {
                MessageId = m.TryGetProperty("internetMessageId", out var mid) ? mid.GetString() ?? m.GetProperty("id").GetString()! : m.GetProperty("id").GetString()!,
                From = from, Subject = m.GetProperty("subject").GetString() ?? "", ReceivedUtc = received, Body = content,
                ProviderRef = m.GetProperty("id").GetString() ?? "",
                WebLink = m.TryGetProperty("webLink", out var wl) ? wl.GetString() ?? "" : "",
                IsBulk = m.TryGetProperty("internetMessageHeaders", out var hs) && hs.ValueKind == JsonValueKind.Array &&
                         hs.EnumerateArray().Any(h => h.TryGetProperty("name", out var n) &&
                             (n.GetString()!.Equals("List-Unsubscribe", StringComparison.OrdinalIgnoreCase) ||
                              (n.GetString()!.Equals("Precedence", StringComparison.OrdinalIgnoreCase) &&
                               (h.GetProperty("value").GetString() ?? "").Contains("bulk", StringComparison.OrdinalIgnoreCase))))
            });
            if (received > (account.LastReceivedUtc ?? DateTime.MinValue)) account.LastReceivedUtc = received;
        }
        if (list.Count == 0 && account.LastReceivedUtc is null) account.LastReceivedUtc = DateTime.UtcNow;
        return list;
    }

    public async Task MoveToTrashAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(email.ProviderRef)) throw new InvalidOperationException("This email was processed before LifeHub 1.2, so it can't be found again.");
        if (await _graph.TryGetJsonAsync(account.Id, $"me/messages/{Uri.EscapeDataString(email.ProviderRef)}?$select=id", ct) is null) return; // already gone
        await _graph.PostJsonAsync(account.Id, $"me/messages/{Uri.EscapeDataString(email.ProviderRef)}/move", new { destinationId = "deleteditems" }, ct);
    }

    public async Task<EmailPresence> GetPresenceAsync(EmailAccount account, EmailRecord email, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(email.ProviderRef)) return EmailPresence.Unknown;
        var m = await _graph.TryGetJsonAsync(account.Id, $"me/messages/{Uri.EscapeDataString(email.ProviderRef)}?$select=parentFolderId", ct);
        if (m is null) return EmailPresence.Deleted;
        var parent = m.Value.GetProperty("parentFolderId").GetString();
        var deleted = await _graph.GetJsonAsync(account.Id, "me/mailFolders/deleteditems?$select=id", ct);
        if (parent == deleted.GetProperty("id").GetString()) return EmailPresence.Deleted;
        var inbox = await _graph.GetJsonAsync(account.Id, "me/mailFolders/inbox?$select=id", ct);
        return parent == inbox.GetProperty("id").GetString() ? EmailPresence.Present : EmailPresence.Archived;
    }
}
