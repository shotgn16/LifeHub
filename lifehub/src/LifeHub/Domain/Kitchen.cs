namespace LifeHub.Domain;

public enum StorageLocation { Fridge, Freezer, Cupboard, Pantry, Other }
public enum FoodEventKind { Purchased, Used, Wasted, Frozen, Defrosted }

/// <summary>Something physically in the house right now.</summary>
public sealed class PantryItem : Entity
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Other";
    public StorageLocation Location { get; set; } = StorageLocation.Fridge;
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "";
    public DateOnly? UseBy { get; set; }
    public DateOnly AddedOn { get; set; }
    public DateOnly? FrozenOn { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public string Notes { get; set; } = "";
    public Guid? StapleId { get; set; }
    public string? Barcode { get; set; }
    public DateOnly? LastRemindedOn { get; set; }

    public int? DaysLeft(DateOnly today) => UseBy is { } d ? d.DayNumber - today.DayNumber : null;
}

/// <summary>Something you always want in the house. Drives automatic shopping list entries.</summary>
public sealed class StapleItem : Entity
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Other";
    public StorageLocation DefaultLocation { get; set; } = StorageLocation.Cupboard;
    /// <summary>Add to the shopping list when the quantity in the house drops below this.</summary>
    public decimal MinQuantity { get; set; } = 1;
    public decimal BuyQuantity { get; set; } = 1;
    public string Unit { get; set; } = "";
    /// <summary>Typical shelf life when bought, used to pre-fill use-by dates.</summary>
    public int? ShelfLifeDays { get; set; }
    /// <summary>Also add when the learned buying interval says you're about to run out.</summary>
    public bool PredictFromHistory { get; set; } = true;
    /// <summary>Other names that count as this staple (e.g. "semi-skimmed milk" for Milk).</summary>
    public List<string> Aliases { get; set; } = new();

    public bool Matches(string name)
    {
        var n = name.Trim();
        return n.Equals(Name, StringComparison.OrdinalIgnoreCase) ||
               Aliases.Any(a => n.Equals(a, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ShoppingItem : Entity
{
    public string Name { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "Other";
    public bool Checked { get; set; }
    /// <summary>Manual, Low stock, Predicted, Expired, AI, API</summary>
    public string Reason { get; set; } = "Manual";
    public Guid? StapleId { get; set; }
    public string? Barcode { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>History used for buying-frequency predictions and waste stats.</summary>
public sealed class FoodEvent : Entity
{
    public string Name { get; set; } = "";
    public Guid? StapleId { get; set; }
    public FoodEventKind Kind { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly Date { get; set; }
}

/// <summary>What LifeHub remembers about a barcode (learned from you or Open Food Facts).</summary>
public sealed class ProductInfo : Entity
{
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Brand { get; set; } = "";
    /// <summary>Pack size as printed, e.g. "2 l" (display only).</summary>
    public string PackSize { get; set; } = "";
    public string Category { get; set; } = "Other";
    public StorageLocation DefaultLocation { get; set; } = StorageLocation.Cupboard;
    /// <summary>Learned from the use-by dates you enter.</summary>
    public int? ShelfLifeDays { get; set; }
    /// <summary>The staple this product counts towards (e.g. any brand of 2L milk → "Milk").</summary>
    public Guid? StapleId { get; set; }
    /// <summary>openfoodfacts or manual</summary>
    public string Source { get; set; } = "manual";
    /// <summary>True once you've checked/edited the details; known products are then handled instantly.</summary>
    public bool Confirmed { get; set; }
    public int ScanCount { get; set; }
    public DateTime? LastScannedUtc { get; set; }
}
