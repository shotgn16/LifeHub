namespace LifeHub.Infrastructure;

/// <summary>
/// All "what time is it" questions go through here so the app uses the household's
/// time zone (Settings → General) even when the server or container runs in UTC.
/// </summary>
public sealed class Clock
{
    private readonly SettingsService _settings;
    private TimeZoneInfo _zone;

    public Clock(SettingsService settings)
    {
        _settings = settings;
        _zone = Resolve();
        settings.Changed += () => _zone = Resolve();
    }

    private TimeZoneInfo Resolve()
    {
        var id = _settings.Current.General.TimeZone;
        try { return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Local; }
    }

    public TimeZoneInfo Zone => _zone;

    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);
    public DateOnly Today => DateOnly.FromDateTime(Now);

    public DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
    public DateTime ToUtc(DateTime local)
    {
        var l = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(l)) l = l.AddHours(1); // inside the spring-forward gap
        return TimeZoneInfo.ConvertTimeToUtc(l, Zone);
    }

    /// <summary>Offset string such as +01:00 for the given local time (used for ISO timestamps sent to HA / Graph).</summary>
    public string OffsetFor(DateTime local)
    {
        var off = Zone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        return (off < TimeSpan.Zero ? "-" : "+") + off.ToString(@"hh\:mm");
    }

    public string Iso(DateTime local) => local.ToString("yyyy-MM-ddTHH:mm:ss") + OffsetFor(local);
}
