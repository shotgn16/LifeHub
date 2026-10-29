using LifeHub.Integrations.HomeAssistant;

namespace LifeHub.Services;

/// <summary>
/// Stores in-app notices and pushes them to phones — through Home Assistant's Companion app, LifeHub's own
/// web push, or both (Settings → Notifications).
/// </summary>
public sealed class NotificationService
{
    private readonly IRepository<Notice> _repo;
    private readonly HomeAssistantClient _ha;
    private readonly WebPushService _push;
    private readonly SettingsService _settings;

    public NotificationService(IRepository<Notice> repo, HomeAssistantClient ha, WebPushService push, SettingsService settings)
    {
        _repo = repo; _ha = ha; _push = push; _settings = settings;
    }

    /// <summary>The channel actually used: falls back to LifeHub's own push when HA has no phones set up.</summary>
    public NotifyChannel EffectiveChannel
    {
        get
        {
            var s = _settings.Current;
            var haReady = _ha.IsConfigured && (s.HomeAssistant.NotifyServices.Count > 0 || !string.IsNullOrWhiteSpace(s.HomeAssistant.NotifyService));
            return s.Notifications.Channel == NotifyChannel.HomeAssistant && !haReady && _push.Devices.All().Count > 0
                ? NotifyChannel.LifeHub : s.Notifications.Channel;
        }
    }

    public IRepository<Notice> Repo => _repo;

    public IReadOnlyList<Notice> Recent(int max = 30) => _repo.All().OrderByDescending(n => n.CreatedUtc).Take(max).ToList();

    public int UnreadCount => _repo.Where(n => !n.Read).Count;

    /// <summary>Sends a notice unless one with the same dedupe key already exists. Returns true if sent.</summary>
    public async Task<bool> NotifyAsync(string title, string message, NoticeLevel level = NoticeLevel.Info, string link = "", string dedupeKey = "",
        bool push = true, string? tag = null, IEnumerable<string>? onlyTo = null, string? person = null)
    {
        // Household-level messages (backups, upgrades, a job running for nobody in particular) go to everyone.
        if (UserScope.UserId is null)
        {
            var sent = false;
            foreach (var user in _settings.Users)
                using (UserScope.As(user.Id))
                    sent |= await NotifyAsync(title, message, level, link, dedupeKey, push, tag, onlyTo, person);
            return sent;
        }
        if (!string.IsNullOrEmpty(dedupeKey) && _repo.Where(n => n.DedupeKey == dedupeKey).Count > 0) return false;
        await _repo.UpsertAsync(new Notice { Title = title, Message = message, Level = level, Link = link, DedupeKey = dedupeKey });
        if (!push) return true;
        var channel = EffectiveChannel;
        if (channel is NotifyChannel.HomeAssistant or NotifyChannel.Both)
            await _ha.NotifyAsync(title, message, string.IsNullOrEmpty(link) ? null : link, tag, onlyTo);
        if (channel is NotifyChannel.LifeHub or NotifyChannel.Both)
            await _push.SendAsync(title, message, link, tag, person);
        return true;
    }

    /// <summary>"• a\n• b\n+3 more" — readable on a phone lock screen.</summary>
    public static string Bullets(IEnumerable<string> lines, int max = 5)
    {
        var all = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        var shown = all.Take(max).Select(l => "• " + (l.Length > 60 ? l[..57].TrimEnd() + "…" : l));
        return string.Join("\n", all.Count > max ? shown.Append($"   +{all.Count - max} more") : shown);
    }

    /// <summary>"Alex Smith-Jones" → "Alex".</summary>
    public static string FirstName(string name) => name.Trim().Split(' ', 2)[0];

    public async Task MarkAllReadAsync()
    {
        var unread = _repo.Where(n => !n.Read);
        foreach (var n in unread) n.Read = true;
        await _repo.UpsertManyAsync(unread);
    }

    public Task PruneAsync(int days = 60)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        return _repo.DeleteManyAsync(_repo.Where(n => n.CreatedUtc < cutoff).Select(n => n.Id));
    }
}
