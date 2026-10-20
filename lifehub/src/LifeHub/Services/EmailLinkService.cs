using LifeHub.Integrations.Email;

namespace LifeHub.Services;

/// <summary>
/// Keeps emails and the tasks created from them in step:
/// deleting the task moves the email to the mailbox's bin, and deleting the email (in LifeHub or in
/// your mail app) removes its open tasks. Nothing is ever permanently deleted from your mailbox.
/// </summary>
public sealed class EmailLinkService
{
    private readonly TaskService _tasks;
    private readonly IRepository<EmailRecord> _emails;
    private readonly IEnumerable<IEmailProvider> _providers;
    private readonly SettingsService _settings;
    private readonly NotificationService _notices;
    private readonly ILogger<EmailLinkService> _log;

    public EmailLinkService(TaskService tasks, IRepository<EmailRecord> emails, IEnumerable<IEmailProvider> providers,
        SettingsService settings, NotificationService notices, ILogger<EmailLinkService> log)
    {
        _tasks = tasks; _emails = emails; _providers = providers; _settings = settings; _notices = notices; _log = log;
    }

    public EmailRecord? EmailFor(TaskItem t) =>
        t.Source == "email" && Guid.TryParse(t.SourceRef, out var id) ? _emails.Get(id) : null;

    /// <summary>All emails behind a task (a stacked task can have several).</summary>
    public List<EmailRecord> EmailsFor(TaskItem t)
    {
        var ids = new List<Guid>();
        if (t.Source == "email" && Guid.TryParse(t.SourceRef, out var first)) ids.Add(first);
        ids.AddRange(t.LinkedEmailIds);
        return ids.Distinct().Select(id => _emails.Get(id)).Where(e => e is not null).Select(e => e!).ToList();
    }

    public IReadOnlyList<TaskItem> TasksFor(Guid emailId)
    {
        var key = emailId.ToString();
        return _tasks.Repo.Where(t => (t.Source == "email" && t.SourceRef == key) || t.LinkedEmailIds.Contains(emailId));
    }

    /// <summary>Emails that would go to the bin if this task were deleted (not shared with another open task).</summary>
    private List<EmailRecord> EmailsToBin(TaskItem t) =>
        !_settings.Current.Email.DeleteEmailWithTask ? new() :
        EmailsFor(t).Where(e => e.DeletedUtc is null && !TasksFor(e.Id).Any(o => o.Id != t.Id && o.IsOpen)).ToList();

    /// <summary>True when deleting this task would also move its email(s) to the bin.</summary>
    public bool WillDeleteEmail(TaskItem t) => EmailsToBin(t).Count > 0;

    private (IEmailProvider Provider, EmailAccount Account)? Resolve(EmailRecord email)
    {
        var account = _settings.Current.Email.Accounts.FirstOrDefault(a => a.Id == email.AccountId);
        var provider = account is null ? null : _providers.FirstOrDefault(p => p.Kind == account.Kind);
        return account is null || provider is null ? null : (provider, account);
    }

    private async Task MoveToBinAsync(EmailRecord email, CancellationToken ct)
    {
        if (email.DeletedUtc is not null) return;
        var r = Resolve(email) ?? throw new InvalidOperationException("The mailbox for this email is no longer set up.");
        await r.Provider.MoveToTrashAsync(r.Account, email, ct);
        email.DeletedUtc = DateTime.UtcNow;
        await _emails.UpsertAsync(email);
    }

    /// <summary>Deletes a task; moves its email to the bin when it was the email's last open task. Returns a message for the UI.</summary>
    public async Task<string> DeleteTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        var task = _tasks.Repo.Get(taskId);
        if (task is null) return "Task already deleted";
        var emails = EmailsToBin(task);
        await _tasks.DeleteAsync(taskId);
        if (emails.Count == 0) return "Task deleted";
        var failed = new List<string>();
        foreach (var email in emails)
        {
            try { await MoveToBinAsync(email, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Couldn't move email {Id} to the bin", email.Id);
                failed.Add(ex.Message);
            }
        }
        var moved = emails.Count - failed.Count;
        if (failed.Count == 0) return emails.Count == 1 ? "Task deleted and its email moved to the bin" : $"Task deleted and its {moved} emails moved to the bin";
        return $"Task deleted; {moved} email(s) moved, {failed.Count} couldn't be: {failed[0]}";
    }

    /// <summary>Moves an email to the bin and deletes its open tasks (if that option is on).</summary>
    public async Task<string> DeleteEmailAsync(Guid emailId, CancellationToken ct = default)
    {
        var email = _emails.Get(emailId);
        if (email is null) return "Email not found";
        await MoveToBinAsync(email, ct);
        var removed = await RemoveTasksAsync(email);
        return removed == 0 ? "Email moved to the bin" : $"Email moved to the bin and {removed} task(s) removed";
    }

    /// <summary>
    /// Removes the email from its open tasks: a stacked task just loses that occurrence; a task with no
    /// other live emails is deleted. Returns how many tasks were deleted.
    /// </summary>
    private async Task<int> RemoveTasksAsync(EmailRecord email)
    {
        if (!_settings.Current.Email.DeleteTasksWithEmail) return 0;
        var deleted = 0;
        foreach (var t in TasksFor(email.Id).Where(t => t.IsOpen).ToList())
        {
            var others = EmailsFor(t).Where(e => e.Id != email.Id && e.DeletedUtc is null).ToList();
            if (others.Count == 0)
            {
                await _tasks.DeleteAsync(t.Id);
                deleted++;
                continue;
            }
            t.LinkedEmailIds.Remove(email.Id);
            if (t.SourceRef == email.Id.ToString())
            {
                t.SourceRef = others[0].Id.ToString();
                t.LinkedEmailIds.Remove(others[0].Id);
            }
            t.Occurrences = Math.Max(1, t.Occurrences - 1);
            await _tasks.Repo.UpsertAsync(t);
        }
        return deleted;
    }

    /// <summary>
    /// Called by the email sync: for emails that still have open tasks, checks whether you've deleted the
    /// email in your mail app; if so, removes the tasks. Archived emails are left alone. Checks a few per run.
    /// </summary>
    public async Task<int> SyncDeletionsAsync(CancellationToken ct)
    {
        if (!_settings.Current.Email.DeleteTasksWithEmail) return 0;
        var openRefs = _tasks.Repo.Where(t => t.IsOpen)
            .SelectMany(t => t.LinkedEmailIds.Select(i => i.ToString()).Append(t.Source == "email" ? t.SourceRef : ""))
            .Where(x => x.Length > 0).ToHashSet();
        var candidates = _emails.Where(e => e.DeletedUtc is null && openRefs.Contains(e.Id.ToString()))
            .OrderBy(e => e.LastCheckedUtc ?? DateTime.MinValue).Take(10).ToList();
        var removedTotal = 0;
        foreach (var email in candidates)
        {
            if (ct.IsCancellationRequested) break;
            var r = Resolve(email);
            if (r is null) continue;
            try
            {
                var presence = await r.Value.Provider.GetPresenceAsync(r.Value.Account, email, ct);
                email.LastCheckedUtc = DateTime.UtcNow;
                if (presence == EmailPresence.Deleted)
                {
                    email.DeletedUtc = DateTime.UtcNow;
                    var titles = TasksFor(email.Id).Where(t => t.IsOpen).Select(t => t.Title).ToList();
                    var removed = await RemoveTasksAsync(email);
                    removedTotal += removed;
                    if (removed > 0)
                        await _notices.NotifyAsync($"Removed {removed} task(s) — email deleted",
                            $"\"{email.Subject}\" was deleted in your mailbox, so I removed: {string.Join(", ", titles)}",
                            NoticeLevel.Info, "tasks", $"emaildel:{email.Id}", push: false);
                }
                await _emails.UpsertAsync(email);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log.LogDebug(ex, "Presence check failed for {Id}", email.Id);
                email.LastCheckedUtc = DateTime.UtcNow;
                await _emails.UpsertAsync(email);
            }
        }
        return removedTotal;
    }
}
