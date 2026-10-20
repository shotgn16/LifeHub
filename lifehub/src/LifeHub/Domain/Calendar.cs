namespace LifeHub.Domain;

/// <summary>A calendar event from any source (HA calendar entity or Microsoft Graph), in local time.</summary>
public sealed class CalendarEvent
{
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool AllDay { get; set; }
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>Calendar entity id or email account name.</summary>
    public string Source { get; set; } = "";
}
