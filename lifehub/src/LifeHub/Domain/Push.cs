namespace LifeHub.Domain;

/// <summary>A browser/phone that receives LifeHub's own push notifications (Web Push).</summary>
public sealed class PushDevice : Entity
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string Name { get; set; } = "";
    public string UserAgent { get; set; } = "";
    /// <summary>HA person this device belongs to (for "while you're out" messages).</summary>
    public string Person { get; set; } = "";
    public DateTime? LastOkUtc { get; set; }
    public string LastError { get; set; } = "";
}
