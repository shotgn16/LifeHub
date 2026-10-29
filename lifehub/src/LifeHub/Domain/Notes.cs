namespace LifeHub.Domain;

/// <summary>A quick note dropped into LifeHub. The AI files it into a category; your text is never changed.</summary>
[Owned(OwnScope.Personal)]
public sealed class Note : Entity, IShareable
{
    public string Text { get; set; } = "";
    public string Category { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    /// <summary>A short heading the AI suggests (shown above the note; your text is untouched).</summary>
    public string Title { get; set; } = "";
    public bool Pinned { get; set; }
    public bool Archived { get; set; }
    /// <summary>Visible to everyone in the household (notes are private by default).</summary>
    public bool Shared { get; set; }
    /// <summary>You chose the category yourself, so the AI leaves it alone when tidying up.</summary>
    public bool CategoryLocked { get; set; }
    /// <summary>"", "sorting", "done", "failed" (the AI couldn't be reached; a simple rule picked the category).</summary>
    public string AiStatus { get; set; } = "";
    public string AiError { get; set; } = "";
    /// <summary>Where it came from: "web", "api", "share", "capture".</summary>
    public string Source { get; set; } = "web";
    public string AuthorName { get; set; } = "";
}
