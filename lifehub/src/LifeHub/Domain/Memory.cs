namespace LifeHub.Domain;

/// <summary>
/// Something LifeHub's AI should keep in mind: "bins go out Tuesday night", "Sam is vegetarian".
/// You add these yourself, or confirm ones the AI suggests; it never saves one on its own.
/// </summary>
[Owned(OwnScope.Personal)]
public sealed class MemoryFact : Entity, IShareable
{
    public string Text { get; set; } = "";
    /// <summary>Everyone in the household's AI uses it (otherwise only yours does).</summary>
    public bool Shared { get; set; }
    /// <summary>Where it came from: "you", "ask", "telegram".</summary>
    public string Source { get; set; } = "you";
}
