using LifeHub.Integrations.HomeAssistant;

namespace LifeHub.Services;

/// <summary>Stores in-app notices and forwards them to the HA notify service (phone push).</summary>
public sealed class NotificationService
{
    private readonly IRepository<Notice> _repo;
    private readonly HomeAssistantClient _ha;

    public NotificationService(IRepository<Notice> repo, HomeAssistantClient ha)
    {
        _repo = repo; _ha = ha;
    }

    public IRepository<Notice> Repo => _repo;

    public IReadOnlyList<Notice> Recent(int max = 30) => _repo.All().OrderByDescending(n => n.CreatedUtc).Take(max).ToList();

    public int UnreadCount => _repo.Where(n => !n.Read).Count;

    /// <summary>Sends a notice unless one with the same dedupe key already exists. Returns true if sent.</summary>
    public async Task<bool> NotifyAsync(string title, string message, NoticeLevel level = NoticeLevel.Info, string link = "", string dedupeKey = "",
        bool push = true, string? tag = null, IEnumerable<string>? onlyTo = null)
    {
        if (!string.IsNullOrEmpty(dedupeKey) && _repo.Where(n => n.DedupeKey == dedupeKey).Count > 0) return false;
        await _repo.UpsertAsync(new Notice { Title = title, Message = message, Level = level, Link = link, DedupeKey = dedupeKey });
        if (push) await _ha.NotifyAsync(title, message, string.IsNullOrEmpty(link) ? null : link, tag, onlyTo);
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
