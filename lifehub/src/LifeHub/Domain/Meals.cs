namespace LifeHub.Domain;

public enum MealEffort { Easy, Medium, Involved }

public sealed class RecipeIngredient
{
    public string Name { get; set; } = "";
    public decimal? Quantity { get; set; }
    public string Unit { get; set; } = "";
    public bool Optional { get; set; }
    public string Notes { get; set; } = "";

    public override string ToString() =>
        (Quantity is { } q ? Components.Shared.Fmt.Qty(q, Unit) + " " : "") + Name + (Optional ? " (optional)" : "") +
        (string.IsNullOrWhiteSpace(Notes) ? "" : $", {Notes}");
}

[Owned(OwnScope.Meals)]
public sealed class Recipe : Entity
{
    public string Name { get; set; } = "";
    public int Servings { get; set; } = 2;
    /// <summary>Total time (prep + cook).</summary>
    public int? Minutes { get; set; }
    public MealEffort Effort { get; set; } = MealEffort.Medium;
    public List<RecipeIngredient> Ingredients { get; set; } = new();
    /// <summary>One step per line.</summary>
    public string Steps { get; set; } = "";
    /// <summary>e.g. gluten free, vegetarian, batch cook, freezes well.</summary>
    public List<string> Tags { get; set; } = new();
    public string SourceUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>Set when the AI drafted or changed it (shown in the AI colour until you edit it).</summary>
    public bool AiGenerated { get; set; }
    /// <summary>The recipe this one was adapted from (e.g. the gluten-free version of X).</summary>
    public Guid? VariantOf { get; set; }
    public string VariantNote { get; set; } = "";
}

[Owned(OwnScope.Meals)]
public sealed class MealPlanEntry : Entity
{
    public DateOnly Date { get; set; }
    public string Slot { get; set; } = "Dinner";
    public Guid? RecipeId { get; set; }
    /// <summary>Shown when there's no recipe ("Takeaway", "Leftovers", "Out with friends").</summary>
    public string Title { get; set; } = "";
    public int Servings { get; set; } = 2;
    public string Notes { get; set; } = "";
    public bool Cooked { get; set; }
}

/// <summary>Your note about how much energy a day leaves (overrides LifeHub's guess from the calendar).</summary>
[Owned(OwnScope.Meals)]
public sealed class MealDayNote : Entity
{
    public DateOnly Date { get; set; }
    /// <summary>low · normal · high (empty = automatic)</summary>
    public string Energy { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>An AI idea for the meal plan. Nothing changes until you press Apply.</summary>
[Owned(OwnScope.Meals)]
public sealed class MealSuggestion : Entity
{
    /// <summary>move · tweak · use-up · diet · tip</summary>
    public string Kind { get; set; } = "tip";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public Guid? EntryId { get; set; }
    public DateOnly? NewDate { get; set; }
    public Guid? RecipeId { get; set; }
    /// <summary>For tweaks: the adapted recipe (saved as a variant when applied).</summary>
    public Recipe? Proposed { get; set; }
    public string Status { get; set; } = "pending";
    public DateOnly PlanFrom { get; set; }
    public DateOnly PlanTo { get; set; }
}
