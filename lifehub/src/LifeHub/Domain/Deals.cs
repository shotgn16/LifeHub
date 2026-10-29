namespace LifeHub.Domain;

/// <summary>An offer found at a store near you (from its offers page, a supermarket email, a leaflet photo or pasted text).</summary>
[Owned(OwnScope.Global)]
public sealed class DealOffer : Entity
{
    public string Store { get; set; } = "";
    public string Item { get; set; } = "";
    public decimal? Price { get; set; }
    public decimal? WasPrice { get; set; }
    /// <summary>"3 for £5", "Half price", "Clubcard price", "Super 6"…</summary>
    public string Offer { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Food { get; set; } = true;
    public DateOnly? ValidUntil { get; set; }
    public string Url { get; set; } = "";
    /// <summary>web · email · leaflet · paste</summary>
    public string Source { get; set; } = "web";
    public DateTime FoundUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Why it matters to you: "on your shopping list", "staple", "in Chicken curry"…</summary>
    public List<string> Matches { get; set; } = new();
    /// <summary>Dietary requirement it may clash with ("gluten free"), if any.</summary>
    public string DietWarning { get; set; } = "";
    public int Score { get; set; }
    public bool Dismissed { get; set; }
    public bool AddedToList { get; set; }

    public string Key => $"{Store}|{Item}".ToLowerInvariant();
}

/// <summary>An idea from this week's deals: a meal on a day that suits it, something to stock up on, or when to shop.</summary>
[Owned(OwnScope.Meals)]
public sealed class DealIdea : Entity
{
    /// <summary>meal · ingredient · trip</summary>
    public string Kind { get; set; } = "meal";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Store { get; set; } = "";
    public DateOnly? Day { get; set; }
    public string Time { get; set; } = "";
    public List<Guid> DealIds { get; set; } = new();
    /// <summary>open · planned · added · dismissed</summary>
    public string Status { get; set; } = "open";
    public string Model { get; set; } = "";
}
