namespace LifeHub.Domain;

/// <summary>A change a phone made while offline that has already been applied (so a retry never applies it twice).</summary>
[Owned(OwnScope.Personal)]
public sealed class SyncReceipt : Entity
{
    public string ClientId { get; set; } = "";
    public string Type { get; set; } = "";
    public string Result { get; set; } = "";
}
