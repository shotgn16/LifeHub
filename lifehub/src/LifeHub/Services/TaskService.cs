namespace LifeHub.Services;

public sealed class TaskService
{
    private readonly IRepository<TaskItem> _repo;
    private readonly Clock _clock;

    public TaskService(IRepository<TaskItem> repo, Clock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public IRepository<TaskItem> Repo => _repo;

    public IReadOnlyList<TaskItem> Open() => _repo.Where(t => t.IsOpen);

    /// <summary>Open tasks sorted the way you'd want to work through them.</summary>
    public IEnumerable<TaskItem> Ordered(IEnumerable<TaskItem> tasks) => tasks
        .OrderBy(t => t.State == TaskState.Done)
        .ThenBy(t => t.DueAt ?? DateTime.MaxValue)
        .ThenByDescending(t => t.Priority)
        .ThenBy(t => t.CreatedUtc);

    public IReadOnlyList<TaskItem> DueToday()
    {
        var now = _clock.Now;
        var today = _clock.Today;
        return Ordered(_repo.Where(t => t.IsOpen && ((t.DueDate is { } d && d <= today) ||
                                                     (t.ScheduledStart is { } s && DateOnly.FromDateTime(s) == today)))).ToList();
    }

    public int OverdueCount() { var now = _clock.Now; return _repo.Where(t => t.IsOverdue(now)).Count; }

    public Task SaveAsync(TaskItem task)
    {
        task.Title = task.Title.Trim();
        if (task.State == TaskState.Done && task.CompletedUtc is null) task.CompletedUtc = DateTime.UtcNow;
        if (task.State != TaskState.Done) task.CompletedUtc = null;
        return _repo.UpsertAsync(task);
    }

    public async Task<TaskItem> QuickAddAsync(string title, string category, DateOnly? due = null, string source = "manual")
    {
        var t = new TaskItem { Title = title.Trim(), Category = category, DueDate = due, Source = source };
        await _repo.UpsertAsync(t);
        return t;
    }

    /// <summary>Completes a task. Recurring tasks spawn their next occurrence so history is kept.</summary>
    public async Task<TaskItem?> CompleteAsync(Guid id)
    {
        var t = _repo.Get(id);
        if (t is null) return null;
        t.State = TaskState.Done;
        t.CompletedUtc = DateTime.UtcNow;
        var updates = new List<TaskItem> { t };
        TaskItem? next = null;

        if (t.Recurrence.Kind != RecurrenceKind.None)
        {
            var baseDate = t.DueDate ?? _clock.Today;
            var nextDue = t.Recurrence.Next(baseDate);
            // If it was very overdue, don't create a backlog of past occurrences.
            while (nextDue < _clock.Today) nextDue = t.Recurrence.Next(nextDue);
            next = Json.Clone(t);
            next.Id = Guid.NewGuid();
            next.CreatedUtc = DateTime.UtcNow;
            next.State = TaskState.Todo;
            next.CompletedUtc = null;
            next.DueDate = nextDue;
            next.ScheduledStart = null;
            next.CalendarEventRef = "";
            next.LastRemindedOn = null;
            next.Source = "recurring";
            next.Occurrences = 1;
            next.LastSeenUtc = null;
            next.LinkedEmailIds = new();
            next.SourceRef = t.Id.ToString();
            foreach (var c in next.Checklist) c.Done = false;
            updates.Add(next);
        }
        await _repo.UpsertManyAsync(updates);
        return next;
    }

    public async Task ReopenAsync(Guid id)
    {
        var t = _repo.Get(id);
        if (t is null) return;
        t.State = TaskState.Todo;
        t.CompletedUtc = null;
        await _repo.UpsertAsync(t);
    }

    public Task DeleteAsync(Guid id) => _repo.DeleteAsync(id);

    // ------------------------------------------------------------ stacking repeats

    private static readonly System.Text.RegularExpressions.Regex NonWord = new(@"[^\p{L}\p{N}]+");

    /// <summary>Lower-case words only, so "Investigate 'ForestChurches' outage" == "investigate forestchurches outage".</summary>
    public static string Normalize(string title) => NonWord.Replace(title.ToLowerInvariant(), " ").Trim();

    /// <summary>Same issue? Identical once normalised, or ≥ 85% the same words with the same first word.</summary>
    public static bool IsSameIssue(string a, string b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        var wa = na.Split(' ').ToHashSet();
        var wb = nb.Split(' ').ToHashSet();
        if (na.Split(' ')[0] != nb.Split(' ')[0]) return false;
        var inter = wa.Intersect(wb).Count();
        return (double)inter / wa.Union(wb).Count() >= 0.85;
    }

    public TaskItem? FindOpenDuplicate(string title, string? category = null) =>
        Ordered(_repo.Where(t => t.IsOpen && IsSameIssue(t.Title, title) && (category is null || t.Category == category)))
            .OrderBy(t => t.CreatedUtc).FirstOrDefault();

    /// <summary>Adds one more occurrence to an existing task instead of creating a duplicate.</summary>
    public async Task<TaskItem> StackAsync(TaskItem existing, string note, Guid? emailId, DateOnly? due, TaskPriority priority)
    {
        existing.Occurrences = Math.Max(1, existing.Occurrences) + 1;
        existing.LastSeenUtc = DateTime.UtcNow;
        if (priority > existing.Priority) existing.Priority = priority;
        if (due is { } d && (existing.DueDate is null || d < existing.DueDate)) existing.DueDate = d;
        if (emailId is { } id && existing.SourceRef != id.ToString() && !existing.LinkedEmailIds.Contains(id)) existing.LinkedEmailIds.Add(id);
        var line = $"• {_clock.Now:dd MMM HH:mm} — seen again{(string.IsNullOrWhiteSpace(note) ? "" : ": " + note.Trim())}";
        existing.Notes = string.IsNullOrWhiteSpace(existing.Notes) ? line : existing.Notes.TrimEnd() + "\n" + line;
        if (existing.Notes.Length > 4000) existing.Notes = existing.Notes[^4000..];
        await _repo.UpsertAsync(existing);
        return existing;
    }

    /// <summary>Stacks open tasks that are the same issue onto the oldest one. Returns how many duplicates were merged.</summary>
    public async Task<int> MergeDuplicatesAsync()
    {
        var open = _repo.Where(t => t.IsOpen).OrderBy(t => t.CreatedUtc).ToList();
        var merged = 0;
        var keep = new List<TaskItem>();
        var remove = new List<Guid>();
        foreach (var t in open)
        {
            var target = keep.FirstOrDefault(k => k.Category == t.Category && IsSameIssue(k.Title, t.Title));
            if (target is null) { keep.Add(t); continue; }
            target.Occurrences = Math.Max(1, target.Occurrences) + Math.Max(1, t.Occurrences);
            target.LastSeenUtc = new[] { target.LastSeenUtc ?? target.CreatedUtc, t.LastSeenUtc ?? t.CreatedUtc }.Max();
            if (t.Priority > target.Priority) target.Priority = t.Priority;
            if (t.DueDate is { } d && (target.DueDate is null || d < target.DueDate)) { target.DueDate = d; target.DueTime = t.DueTime; }
            if (t.Source == "email" && Guid.TryParse(t.SourceRef, out var eid) && target.SourceRef != t.SourceRef && !target.LinkedEmailIds.Contains(eid))
                target.LinkedEmailIds.Add(eid);
            foreach (var id in t.LinkedEmailIds.Where(id => !target.LinkedEmailIds.Contains(id))) target.LinkedEmailIds.Add(id);
            target.Notes = (target.Notes.TrimEnd() + $"\n• {t.CreatedUtc.ToLocalTime():dd MMM HH:mm} — merged duplicate").Trim();
            remove.Add(t.Id);
            merged++;
        }
        if (merged == 0) return 0;
        await _repo.UpsertManyAsync(keep.Where(k => k.Occurrences > 1));
        await _repo.DeleteManyAsync(remove);
        return merged;
    }

    /// <summary>Removes completed tasks older than the given number of days.</summary>
    public Task<int> PurgeCompletedAsync(int olderThanDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var ids = _repo.Where(t => t.State == TaskState.Done && t.CompletedUtc < cutoff).Select(t => t.Id).ToList();
        return _repo.DeleteManyAsync(ids).ContinueWith(_ => ids.Count);
    }
}
