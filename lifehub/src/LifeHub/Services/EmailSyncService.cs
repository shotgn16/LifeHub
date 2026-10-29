using LifeHub.Ai;
using LifeHub.Integrations.Email;

namespace LifeHub.Services;

/// <summary>Polls every enabled mailbox, runs new mail through the AI and stores suggestions.</summary>
public sealed class EmailSyncService : BackgroundService
{
    private readonly SettingsService _settings;
    private readonly IEnumerable<IEmailProvider> _providers;
    private readonly IRepository<EmailRecord> _emails;
    private readonly AiService _ai;
    private readonly IServiceProvider _sp;
    private readonly ILogger<EmailSyncService> _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public DateTime? LastRunUtc { get; private set; }
    public string LastRunSummary { get; private set; } = "Not run yet";

    public EmailSyncService(SettingsService settings, IEnumerable<IEmailProvider> providers, IRepository<EmailRecord> emails,
        AiService ai, IServiceProvider sp, ILogger<EmailSyncService> log)
    {
        _settings = settings; _providers = providers; _emails = emails; _ai = ai; _sp = sp; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var system = UserScope.AsSystem();
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SyncAllAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogError(ex, "Email sync failed"); }
            var minutes = Math.Max(2, _settings.Current.Email.PollMinutes);
            await Task.Delay(TimeSpan.FromMinutes(minutes), stoppingToken);
        }
    }

    /// <summary>Runs a sync now (also used by the "Check now" button). Skips if one is already running.</summary>
    public async Task<string> SyncAllAsync(CancellationToken ct = default)
    {
        if (!await _running.WaitAsync(0, ct)) return "A sync is already running";
        try
        {
            var settings = _settings.Current;
            var accounts = settings.Email.Accounts.Where(a => a.Enabled).ToList();
            if (accounts.Count == 0) return LastRunSummary = "No email accounts enabled";

            int fetched = 0, suggested = 0;
            var errors = new List<string>();
            foreach (var account in accounts)
            {
                // Each mailbox's emails, tasks and suggestions belong to the person whose mailbox it is.
                using var owner = UserScope.As(account.OwnerId == Guid.Empty ? _settings.PrimaryUserId : account.OwnerId);
                var provider = _providers.FirstOrDefault(p => p.Kind == account.Kind);
                if (provider is null) continue;
                var working = Json.Clone(account);
                try
                {
                    var messages = await provider.FetchNewAsync(working, settings.Email, ct);
                    fetched += messages.Count;
                    foreach (var m in messages) suggested += await ProcessAsync(working, m, ct);
                    working.LastError = "";
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _log.LogWarning(ex, "Mailbox {Name} failed", account.Name);
                    working.LastError = ex.Message;
                    errors.Add($"{account.Name}: {ex.Message}");
                }
                working.LastSyncUtc = DateTime.UtcNow;
                // Only copy sync-state fields so edits made in Settings meanwhile aren't lost.
                await _settings.UpdateAsync(s =>
                {
                    var target = s.Email.Accounts.FirstOrDefault(a => a.Id == account.Id);
                    if (target is null) return;
                    target.LastImapUid = working.LastImapUid;
                    target.ImapUidValidity = working.ImapUidValidity;
                    target.LastReceivedUtc = working.LastReceivedUtc;
                    target.LastSyncUtc = working.LastSyncUtc;
                    target.LastError = working.LastError;
                    if (string.IsNullOrEmpty(target.Address)) target.Address = working.Address;
                });
            }

            var removed = 0;
            try { removed = await _sp.GetRequiredService<EmailLinkService>().SyncDeletionsAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { _log.LogWarning(ex, "Deleted-email check failed"); }
            await PruneAsync();
            LastRunUtc = DateTime.UtcNow;
            LastRunSummary = $"{fetched} new email(s), {suggested} suggestion(s)" + (removed > 0 ? $", {removed} task(s) removed for deleted emails" : "") + (errors.Count > 0 ? $"; errors: {string.Join("; ", errors)}" : "");
            return LastRunSummary;
        }
        finally { _running.Release(); }
    }

    private async Task<int> ProcessAsync(EmailAccount account, EmailMessage m, CancellationToken ct)
    {
        if (_emails.Where(e => e.MessageId == m.MessageId && e.AccountId == account.Id).Count > 0) return 0;

        var record = new EmailRecord
        {
            AccountId = account.Id, AccountName = account.Name, MessageId = m.MessageId, From = m.From,
            Subject = m.Subject, ReceivedUtc = m.ReceivedUtc, ProviderRef = m.ProviderRef, WebLink = m.WebLink,
            Snippet = EmailText.ForModel(m.Body, 240).Replace('\n', ' ')
        };

        if (account.IgnoreSenders.Any(x => !string.IsNullOrWhiteSpace(x) && m.From.Contains(x.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            record.Summary = "Ignored (sender on ignore list)";
            await _emails.UpsertAsync(record);
            return 0;
        }

        if (!_ai.IsAvailable)
        {
            record.Summary = "Not analysed (AI disabled)";
            await _emails.UpsertAsync(record);
            return 0;
        }

        try
        {
            var result = await _ai.AnalyzeEmailAsync(m, ct);
            record.Summary = result.Summary;
            record.Importance = result.Importance;
            record.Category = result.Category;
            record.SuggestionCount = result.Suggestions.Count;
            await _emails.UpsertAsync(record);
            foreach (var s in result.Suggestions)
            {
                s.EmailId = record.Id;
                s.OriginText = $"{m.Subject} — {m.From}";
            }
            await _ai.SaveAndAutoApplyAsync(result.Suggestions);
            return result.Suggestions.Count;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            record.Error = ex.Message;
            await _emails.UpsertAsync(record);
            return 0;
        }
    }

    /// <summary>Keeps the processed-email log to the latest 2000 / 90 days (email bodies are never stored).</summary>
    private Task PruneAsync()
    {
        // Never forget an email that still has open tasks (needed for the two-way delete link).
        var linked = _sp.GetRequiredService<TaskService>().Repo.Where(t => t.IsOpen)
            .SelectMany(t => t.LinkedEmailIds.Select(i => i.ToString()).Append(t.SourceRef)).ToHashSet();
        var all = _emails.Where(e => !linked.Contains(e.Id.ToString()));
        var old = all.OrderByDescending(e => e.ReceivedUtc).Skip(2000)
            .Concat(all.Where(e => e.ReceivedUtc < DateTime.UtcNow.AddDays(-90)))
            .Select(e => e.Id).Distinct().ToList();
        return old.Count == 0 ? Task.CompletedTask : _emails.DeleteManyAsync(old);
    }
}
