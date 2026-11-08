using System.Collections.Concurrent;

namespace LifeHub.Services;

/// <summary>When each background job last ran and how it went (shown in Settings → Background tasks).</summary>
public sealed class BackgroundStatus
{
    public sealed record Run(DateTime Utc, bool Ok, string Message);
    private readonly ConcurrentDictionary<string, Run> _runs = new();

    public void Record(string job, bool ok, string message = "") => _runs[job] = new Run(DateTime.UtcNow, ok, message);
    public Run? Last(string job) => _runs.TryGetValue(job, out var r) ? r : null;
}
