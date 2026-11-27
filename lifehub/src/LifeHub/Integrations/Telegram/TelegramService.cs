using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeHub.Ai;

namespace LifeHub.Integrations.Telegram;

/// <summary>A notification channel other than the phone (Telegram today).</summary>
public interface IChatNotifier
{
    /// <summary>Sends the notice to the current person's chat, if they have one and want notifications there.</summary>
    Task SendAsync(Notice notice, CancellationToken ct = default);
}

/// <summary>Someone who messaged the bot but isn't connected to anyone in LifeHub yet.</summary>
public sealed record TelegramPairing(string Code, long ChatId, string Name, string Username, DateTime ExpiresUtc);

/// <summary>
/// The household's Telegram bot. People message it to ask LifeHub things or tell it what to do; LifeHub replies,
/// with buttons to confirm any change. Only chats someone has connected in Settings → Telegram (with the code the
/// bot sends) can use it — anyone else just gets that code. It also sends notifications to people who want them.
/// Uses long polling, so LifeHub doesn't need to be reachable from the internet.
/// </summary>
public sealed class TelegramService : BackgroundService, IChatNotifier
{
    /// <summary>Telegram's API (LIFEHUB_TELEGRAM_API changes it, for tests).</summary>
    public static string ApiBase => (Environment.GetEnvironmentVariable("LIFEHUB_TELEGRAM_API") is { Length: > 0 } a ? a : "https://api.telegram.org").TrimEnd('/');

    private static readonly TimeSpan PairingLife = TimeSpan.FromHours(1);
    private const int MaxPairings = 20;

    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly IServiceProvider _sp;
    private readonly BackgroundStatus _status;
    private readonly ILogger<TelegramService> _log;

    private readonly ConcurrentDictionary<string, TelegramPairing> _pairings = new();
    private readonly ConcurrentDictionary<long, DateTime> _lastCodeSent = new();
    /// <summary>Suggestions behind a reply's buttons: Telegram allows only 64 bytes per button, so they get a short id.</summary>
    private readonly ConcurrentDictionary<string, (Guid User, List<Guid> Ids, DateTime Utc)> _batches = new();
    private long _offset;
    private string _pollingToken = "";

    public TelegramService(SettingsService settings, IHttpClientFactory http, IServiceProvider sp, BackgroundStatus status, ILogger<TelegramService> log)
    {
        _settings = settings; _http = http; _sp = sp; _status = status; _log = log;
    }

    /// <summary>Last problem talking to Telegram, for Settings (empty when all is well).</summary>
    public string LastError { get; private set; } = "";
    public DateTime? LastContactUtc { get; private set; }
    public bool Running => _settings.Household.Telegram.IsReady && _settings.Household.Background.IsOn(BackgroundJobs.Telegram);

    public IReadOnlyList<TelegramPairing> Pairings =>
        _pairings.Values.Where(p => p.ExpiresUtc > DateTime.UtcNow).OrderBy(p => p.ExpiresUtc).ToList();

    public event Action? Changed;

    // ------------------------------------------------------------------ setup

    /// <summary>Checks a bot token with Telegram and saves it. Returns the bot's @username.</summary>
    public async Task<string> ConnectAsync(string token, CancellationToken ct = default)
    {
        token = token.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d{5,}:[A-Za-z0-9_-]{30,}$"))
            throw new InvalidOperationException("That doesn't look like a bot token. It's the long code BotFather sends, like 123456789:ABCdef…");
        var me = await CallAsync(token, "getMe", null, ct);
        var username = me["username"]?.GetValue<string>() ?? "";
        var name = me["first_name"]?.GetValue<string>() ?? username;
        await _settings.UpdateAsync(s =>
        {
            s.Telegram.BotToken = token; s.Telegram.BotUsername = username; s.Telegram.BotName = name; s.Telegram.Enabled = true;
        });
        LastError = "";
        return username;
    }

    public async Task DisconnectBotAsync()
    {
        await _settings.UpdateAsync(s =>
        {
            s.Telegram = new TelegramSettings();
            foreach (var u in s.Users) { u.TelegramChatId = null; u.TelegramName = ""; }
        });
        _pairings.Clear();
        Changed?.Invoke();
    }

    /// <summary>Connects the chat that was given <paramref name="code"/> to a person (themselves, or anyone when an admin approves).</summary>
    public async Task<string> ClaimAsync(string code, Guid userId, CancellationToken ct = default)
    {
        code = new string((code ?? "").Where(char.IsDigit).ToArray());
        if (!_pairings.TryGetValue(code, out var p) || p.ExpiresUtc < DateTime.UtcNow)
            throw new InvalidOperationException("That code isn't right or has run out. Send /start to the bot for a new one.");
        var user = _settings.User(userId) ?? throw new InvalidOperationException("Person not found");
        await _settings.UpdateAsync(s =>
        {
            foreach (var u in s.Users.Where(u => u.TelegramChatId == p.ChatId)) { u.TelegramChatId = null; u.TelegramName = ""; }
            var me = s.Users.First(u => u.Id == userId);
            me.TelegramChatId = p.ChatId;
            me.TelegramName = string.IsNullOrEmpty(p.Username) ? p.Name : $"{p.Name} (@{p.Username})";
        });
        _pairings.TryRemove(code, out _);
        Changed?.Invoke();
        await SendTextAsync(p.ChatId,
            $"Connected — you're {Html(NotificationService.FirstName(user.Name))} in LifeHub. 🎉\n\n" + HelpText(), ct: ct);
        return p.Name;
    }

    public void Decline(string code)
    {
        _pairings.TryRemove(code, out _);
        Changed?.Invoke();
    }

    public async Task UnlinkAsync(Guid userId)
    {
        var chat = _settings.User(userId)?.TelegramChatId;
        await _settings.UpdateUserAsync(userId, u => { u.TelegramChatId = null; u.TelegramName = ""; });
        if (chat is { } c) await TrySendAsync(c, "This chat isn't connected to LifeHub any more.");
    }

    // ------------------------------------------------------------------ notifications

    public async Task SendAsync(Notice notice, CancellationToken ct = default)
    {
        if (!Running) return;
        var me = _settings.Me;
        if (me?.TelegramChatId is not { } chat || !me.TelegramNotify) return;
        var text = $"<b>{Html(notice.Title)}</b>" + (string.IsNullOrWhiteSpace(notice.Message) ? "" : "\n" + Html(notice.Message));
        await TrySendAsync(chat, text, ct);
    }

    /// <summary>"Send a test message" in Settings → Telegram (errors are shown, unlike notifications).</summary>
    public async Task SendTestAsync()
    {
        if (_settings.Me?.TelegramChatId is not { } chat) throw new InvalidOperationException("Connect your chat first");
        await SendTextAsync(chat, "<b>LifeHub test</b>\nIf you can read this, LifeHub can message you here.");
    }

    // ------------------------------------------------------------------ polling

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var failures = 0;
        while (!stop.IsCancellationRequested)
        {
            var t = _settings.Household.Telegram;
            if (!Running)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stop).ContinueWith(_ => { });
                continue;
            }
            if (t.BotToken != _pollingToken) { _pollingToken = t.BotToken; _offset = 0; }
            try
            {
                var args = new JsonObject { ["timeout"] = 25, ["allowed_updates"] = new JsonArray("message", "callback_query") };
                if (_offset != 0) args["offset"] = _offset;
                var updates = await CallAsync(t.BotToken, "getUpdates", args, stop, TimeSpan.FromSeconds(40));
                failures = 0;
                LastError = "";
                LastContactUtc = DateTime.UtcNow;
                foreach (var u in updates.AsArray())
                {
                    if (u is null) continue;
                    _offset = Math.Max(_offset, (u["update_id"]?.GetValue<long>() ?? 0) + 1);
                    try { await HandleAsync(u, stop); }
                    catch (Exception ex) when (!stop.IsCancellationRequested) { _log.LogWarning(ex, "Telegram message failed"); }
                }
                if (updates.AsArray().Count > 0) _status.Record(BackgroundJobs.Telegram, true, $"{updates.AsArray().Count} message(s)");
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failures++;
                LastError = ex.Message;
                _status.Record(BackgroundJobs.Telegram, false, ex.Message);
                _log.LogWarning("Telegram: {Error}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 10 * failures)), stop).ContinueWith(_ => { });
            }
            Prune();
        }
    }

    private void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var (k, v) in _pairings) if (v.ExpiresUtc < now) _pairings.TryRemove(k, out _);
        foreach (var (k, v) in _batches) if (now - v.Utc > TimeSpan.FromDays(2)) _batches.TryRemove(k, out _);
        foreach (var (k, v) in _lastCodeSent) if (now - v > TimeSpan.FromHours(2)) _lastCodeSent.TryRemove(k, out _);
    }

    /// <summary>One update from Telegram: a message or a button press.</summary>
    public async Task HandleAsync(JsonNode update, CancellationToken ct)
    {
        if (update["callback_query"] is { } cb) { await HandleButtonAsync(cb, ct); return; }
        var msg = update["message"];
        if (msg is null) return;
        var chat = msg["chat"];
        // Only one-to-one chats: in a group anyone could read the answers.
        if (chat?["type"]?.GetValue<string>() != "private") return;
        var chatId = chat["id"]!.GetValue<long>();
        var from = msg["from"];
        if (from?["is_bot"]?.GetValue<bool>() == true) return;
        var text = (msg["text"]?.GetValue<string>() ?? "").Trim();

        var user = _settings.Users.FirstOrDefault(u => u.TelegramChatId == chatId);
        if (user is null)
        {
            await SendPairingCodeAsync(chatId, from, ct);
            return;
        }
        using (UserScope.As(user.Id))
            await HandleTextAsync(chatId, user, text, ct);
    }

    private async Task SendPairingCodeAsync(long chatId, JsonNode? from, CancellationToken ct)
    {
        // At most one reply a minute to a stranger, and a limited number waiting, so the bot can't be used to spam.
        if (_lastCodeSent.TryGetValue(chatId, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(1)) return;
        var existing = _pairings.Values.FirstOrDefault(p => p.ChatId == chatId && p.ExpiresUtc > DateTime.UtcNow);
        if (existing is null && _pairings.Count >= MaxPairings) return;
        var name = string.Join(' ', new[] { from?["first_name"]?.GetValue<string>(), from?["last_name"]?.GetValue<string>() }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var pairing = existing ?? NewPairing(chatId, string.IsNullOrWhiteSpace(name) ? "Telegram user" : name, from?["username"]?.GetValue<string>() ?? "");
        _lastCodeSent[chatId] = DateTime.UtcNow;
        Changed?.Invoke();
        await SendTextAsync(chatId,
            "Hi! To connect this chat to LifeHub, open LifeHub → <b>Settings → Telegram</b> and enter this code:\n\n" +
            $"<code>{pairing.Code}</code>\n\nIt works for an hour. Someone who manages your LifeHub can also approve it there.", ct: ct);
    }

    private TelegramPairing NewPairing(long chatId, string name, string username)
    {
        string code;
        do code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        while (_pairings.ContainsKey(code));
        var p = new TelegramPairing(code, chatId, name.Length > 60 ? name[..60] : name, username, DateTime.UtcNow + PairingLife);
        _pairings[code] = p;
        return p;
    }

    private async Task HandleTextAsync(long chatId, UserProfile user, string text, CancellationToken ct)
    {
        var command = text.Split(' ', 2)[0].ToLowerInvariant();
        if (command.Contains('@')) command = command[..command.IndexOf('@')];
        switch (command)
        {
            case "":
                await SendTextAsync(chatId, "I can only read text messages for now.", ct: ct);
                return;
            case "/start" or "/help":
                await SendTextAsync(chatId, HelpText(), ct: ct);
                return;
            case "/today":
                await SendTextAsync(chatId, await TodayAsync(), ct: ct);
                return;
            case "/shopping":
                var items = _sp.GetRequiredService<KitchenService>().Shopping.Where(x => !x.Checked).OrderBy(x => x.Name).ToList();
                await SendTextAsync(chatId, items.Count == 0 ? "The shopping list is empty." :
                    "<b>Shopping list</b>\n" + string.Join("\n", items.Select(x => "• " + Html(x.Quantity > 1 ? $"{x.Name} ×{x.Quantity:0.##}" : x.Name))), ct: ct);
                return;
            case "/stop":
                await UnlinkAsync(user.Id);
                return;
        }

        var ask = _sp.GetRequiredService<AskService>();
        if (!ask.IsAvailable)
        {
            await SendTextAsync(chatId, await WithoutAiAsync(text), ct: ct);
            return;
        }
        await TrySendActionAsync(chatId, "typing", ct);
        AskReply reply;
        try { reply = await ask.AskAsync(text, "telegram", ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await SendTextAsync(chatId, "Sorry, I couldn't do that: " + Html(ex.Message), ct: ct);
            return;
        }
        var pending = reply.Suggestions.Where(s => s.Status == SuggestionStatus.Pending).ToList();
        var body = new StringBuilder(Html(reply.Answer));
        JsonObject? keyboard = null;
        if (pending.Count > 0)
        {
            var batch = NewBatchId();
            _batches[batch] = (user.Id, pending.Select(s => s.Id).ToList(), DateTime.UtcNow);
            body.Append("\n");
            for (var i = 0; i < pending.Count; i++) body.Append($"\n{i + 1}. {Html(Describe(pending[i]))}");
            keyboard = Keyboard(batch, pending);
        }
        await SendTextAsync(chatId, body.ToString(), keyboard, ct);
    }

    private async Task HandleButtonAsync(JsonNode cb, CancellationToken ct)
    {
        var token = _settings.Household.Telegram.BotToken;
        var id = cb["id"]?.GetValue<string>() ?? "";
        var data = cb["data"]?.GetValue<string>() ?? "";
        var msg = cb["message"];
        var chatId = msg?["chat"]?["id"]?.GetValue<long>() ?? 0;
        var messageId = msg?["message_id"]?.GetValue<long>() ?? 0;
        var user = _settings.Users.FirstOrDefault(u => u.TelegramChatId == chatId && chatId != 0);
        var parts = data.Split(':');
        if (user is null || parts.Length < 2 || !_batches.TryGetValue(parts[1], out var batch) || batch.User != user.Id)
        {
            await TryCallAsync(token, "answerCallbackQuery", new JsonObject { ["callback_query_id"] = id, ["text"] = "That's expired — see the AI Inbox in LifeHub." }, ct);
            return;
        }
        using (UserScope.As(user.Id))
        {
            var ai = _sp.GetRequiredService<AiService>();
            var targets = parts[0] switch
            {
                "all" or "skip" => batch.Ids,
                _ when int.TryParse(parts.ElementAtOrDefault(2), out var n) && n >= 0 && n < batch.Ids.Count => new List<Guid> { batch.Ids[n] },
                _ => new List<Guid>()
            };
            var results = new List<string>();
            foreach (var sid in targets)
            {
                var s = ai.Suggestions.Get(sid);
                if (s is null || s.Status != SuggestionStatus.Pending) continue;
                if (parts[0] == "skip") { await ai.RejectAsync(sid); continue; }
                var done = await ai.ApplyAsync(sid, ct: ct);
                if (done is not null) results.Add((done.Status == SuggestionStatus.Applied ? "✓ " : "✗ ") + $"{done.Title} — {done.Result}");
            }
            await TryCallAsync(token, "answerCallbackQuery", new JsonObject
            {
                ["callback_query_id"] = id, ["text"] = parts[0] == "skip" ? "Skipped" : results.Count == 0 ? "Already done" : "Done"
            }, ct);
            // Leave buttons only for what's still waiting.
            var still = batch.Ids.Select(x => ai.Suggestions.Get(x)).Where(x => x?.Status == SuggestionStatus.Pending).Select(x => x!).ToList();
            var markup = still.Count == 0 ? new JsonObject { ["inline_keyboard"] = new JsonArray() } : Keyboard(parts[1], still, batch.Ids);
            await TryCallAsync(token, "editMessageReplyMarkup", new JsonObject { ["chat_id"] = chatId, ["message_id"] = messageId, ["reply_markup"] = markup }, ct);
            if (results.Count > 0) await SendTextAsync(chatId, Html(string.Join("\n", results)), ct: ct);
            else if (parts[0] == "skip") await SendTextAsync(chatId, "OK, I've left those.", ct: ct);
        }
    }

    private static string NewBatchId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

    /// <summary>One button per suggestion (numbered as in the message), then "Do all" and "Skip".</summary>
    private static JsonObject Keyboard(string batch, List<AiSuggestion> pending, List<Guid>? order = null)
    {
        var rows = new JsonArray();
        foreach (var s in pending)
        {
            var index = (order ?? pending.Select(p => p.Id).ToList()).IndexOf(s.Id);
            var label = $"✓ {index + 1}. {Describe(s)}";
            if (label.Length > 60) label = label[..57] + "…";
            rows.Add(new JsonArray(new JsonObject { ["text"] = label, ["callback_data"] = $"do:{batch}:{index}" }));
        }
        var last = new JsonArray();
        if (pending.Count > 1) last.Add(new JsonObject { ["text"] = "✓ Do all", ["callback_data"] = $"all:{batch}" });
        last.Add(new JsonObject { ["text"] = "Skip", ["callback_data"] = $"skip:{batch}" });
        rows.Add(last);
        return new JsonObject { ["inline_keyboard"] = rows };
    }

    private static string Describe(AiSuggestion s)
    {
        var what = s.Type switch
        {
            "shopping" => "Add to shopping: " + s.Title,
            "task" => "Task: " + s.Title,
            "event" => "Calendar: " + s.Title,
            "renewal" => "Renewal: " + s.Title,
            "pantry" => "Kitchen: " + s.Title,
            "memory" => "Remember: " + s.Title,
            "done" => "Tick off: " + s.Title,
            "schedule" => $"{(s.Category ?? "out").Replace('_', ' ')}: {(string.IsNullOrWhiteSpace(s.Title) ? "" : s.Title)}".Trim().TrimEnd(':'),
            _ => s.Title
        };
        var when = new List<string>();
        if (s.Date is { } d) when.Add(d.ToString("ddd d MMM", CultureInfo.GetCultureInfo("en-GB")));
        if (s.Time is { } t) when.Add(s.DurationMinutes is > 0 ? $"{t:HH:mm}–{t.AddMinutes(s.DurationMinutes.Value):HH:mm}" : t.ToString("HH:mm"));
        return when.Count == 0 ? what : $"{what} ({string.Join(" ", when)})";
    }

    private static string HelpText() =>
        "Ask me anything about your LifeHub, or tell me what to do. For example:\n" +
        "• <i>What's due this week?</i>\n• <i>Add milk and eggs</i>\n• <i>I'm out Thursday 2 till 4</i>\n• <i>Remember the bins go out Tuesday night</i>\n\n" +
        "I always check with you before changing anything.\n/today — today at a glance\n/shopping — the shopping list\n/stop — disconnect this chat";

    /// <summary>Today at a glance, without the AI.</summary>
    private async Task<string> TodayAsync()
    {
        var clock = _sp.GetRequiredService<Clock>();
        var schedule = _sp.GetRequiredService<ScheduleService>();
        var tasks = _sp.GetRequiredService<TaskService>();
        var today = clock.Today;
        List<CalendarEvent> events;
        try { events = await schedule.EventsForAsync(today).WaitAsync(TimeSpan.FromSeconds(8)); } catch { events = new(); }
        var sb = new StringBuilder($"<b>{today.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-GB"))}</b>\n{Html(schedule.Summary(today, events))}\n");
        foreach (var p in schedule.Periods(today, events).Where(p => p.Kind is PeriodKind.Out or PeriodKind.Reserved))
            sb.Append($"• {p.Start:HH:mm}–{p.End:HH:mm} {Html(p.Label)}\n");
        foreach (var e in events.Where(e => !e.AllDay).OrderBy(e => e.Start)) sb.Append($"• {e.Start:HH:mm} {Html(e.Title)}\n");
        var due = tasks.DueToday();
        sb.Append(due.Count == 0 ? "\nNothing due today." : "\n<b>Due</b>\n" + string.Join("\n", due.Take(15).Select(t => "• " + Html(t.Title))));
        return sb.ToString();
    }

    /// <summary>With no AI set up: "buy …" adds to the shopping list and "task …" adds a task.</summary>
    private async Task<string> WithoutAiAsync(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var prefix in new[] { "buy ", "shopping ", "add " })
            if (lower.StartsWith(prefix))
            {
                var kitchen = _sp.GetRequiredService<KitchenService>();
                var names = text[prefix.Length..].Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .SelectMany(x => x.Split(" and ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Take(20).ToList();
                foreach (var n in names) await kitchen.AddToShoppingAsync(n, reason: "Telegram");
                return $"Added to the shopping list: {Html(string.Join(", ", names))}";
            }
        foreach (var prefix in new[] { "task ", "todo ", "to do " })
            if (lower.StartsWith(prefix))
            {
                var t = await _sp.GetRequiredService<TaskService>().QuickAddAsync(text[prefix.Length..].Trim(), _settings.Current.General.TaskCategories.FirstOrDefault() ?? "Admin", source: "telegram");
                return $"Task added: {Html(t.Title)}";
            }
        return "To ask questions in plain English, set up an AI in LifeHub (Settings → AI).\n" +
               "Until then: <i>buy milk, eggs</i> adds to the shopping list, <i>task call the plumber</i> adds a task, and /today and /shopping work.";
    }

    // ------------------------------------------------------------------ Telegram API

    private Task SendTextAsync(long chatId, string html, JsonObject? keyboard = null, CancellationToken ct = default)
    {
        var args = new JsonObject { ["chat_id"] = chatId, ["text"] = html.Length > 4000 ? html[..4000] : html, ["parse_mode"] = "HTML", ["disable_web_page_preview"] = true };
        if (keyboard is not null) args["reply_markup"] = keyboard;
        return CallAsync(_settings.Household.Telegram.BotToken, "sendMessage", args, ct);
    }

    private async Task TrySendAsync(long chatId, string html, CancellationToken ct = default)
    {
        try { await SendTextAsync(chatId, html, ct: ct); }
        catch (Exception ex) { _log.LogWarning("Telegram send failed: {Error}", ex.Message); }
    }

    private Task TrySendActionAsync(long chatId, string action, CancellationToken ct) =>
        TryCallAsync(_settings.Household.Telegram.BotToken, "sendChatAction", new JsonObject { ["chat_id"] = chatId, ["action"] = action }, ct);

    private async Task TryCallAsync(string token, string method, JsonObject args, CancellationToken ct)
    {
        try { await CallAsync(token, method, args, ct); }
        catch (Exception ex) { _log.LogDebug("Telegram {Method}: {Error}", method, ex.Message); }
    }

    private async Task<JsonNode> CallAsync(string token, string method, JsonObject? args, CancellationToken ct, TimeSpan? timeout = null)
    {
        var client = _http.CreateClient("telegram");
        client.Timeout = timeout ?? TimeSpan.FromSeconds(20);
        using var content = new StringContent((args ?? new JsonObject()).ToJsonString(), Encoding.UTF8, "application/json");
        HttpResponseMessage res;
        try { res = await client.PostAsync($"{ApiBase}/bot{token}/{method}", content, ct); }
        catch (HttpRequestException ex) { throw new InvalidOperationException("Can't reach Telegram: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("Telegram didn't answer in time."); }
        using (res)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            JsonNode? j = null;
            try { j = JsonNode.Parse(body); } catch (JsonException) { }
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound && method == "getMe")
                throw new InvalidOperationException("Telegram didn't accept that token. Copy it again from BotFather.");
            if (j?["ok"]?.GetValue<bool>() != true)
            {
                var desc = j?["description"]?.GetValue<string>() ?? $"HTTP {(int)res.StatusCode}";
                if (res.StatusCode == HttpStatusCode.Unauthorized) desc = "the bot token was rejected — reconnect it in Settings → Telegram";
                if (res.StatusCode == HttpStatusCode.Conflict) desc = "another program is reading this bot's messages (only one LifeHub can use a bot)";
                throw new InvalidOperationException("Telegram: " + desc);
            }
            return j["result"]!;
        }
    }

    private static string Html(string s) => WebUtility.HtmlEncode(s ?? "");
}
