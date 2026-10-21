using System.Text.RegularExpressions;

namespace LifeHub.Services;

public enum FreshState { Fresh, UseSoon, PastBest, CheckFirst, Expired, NoDate }

/// <summary>Typical shelf-life behaviour for a kind of food.</summary>
public sealed record FreshProfile(string Name, DateKind Kind, int ShelfDays, int? OpenedDays, int GraceDays, StorageLocation Location);

/// <summary>Where an item is in its life: state, label and a 0–1 position for the gradient bar.</summary>
public sealed record FreshInfo(FreshState State, string Label, string Detail, double Position, DateOnly? EffectiveDate, DateOnly? CheckBy)
{
    public string Css => State switch
    {
        FreshState.Fresh => "ok",
        FreshState.UseSoon => "warn",
        FreshState.PastBest => "soft",
        FreshState.CheckFirst => "danger",
        FreshState.Expired => "danger",
        _ => ""
    };

    /// <summary>Should this be flagged in "use it up" lists and reminders?</summary>
    public bool NeedsAttention => State is FreshState.UseSoon or FreshState.CheckFirst or FreshState.Expired;
}

/// <summary>
/// Use-by food is a cliff edge (unsafe after the date). Best-before food fades: eggs, cereal, pasta,
/// tins, crisps… are usually fine for a while after the date, so they move through
/// Fresh → Best soon → Past best (usually fine) → Check before using.
/// </summary>
public static class Freshness
{
    // Conservative, typical guidance. Staples can override every value.
    private static readonly (string Pattern, FreshProfile Profile)[] Profiles =
    {
        (@"\beggs?\b(?!\s*noodle)", new("Eggs", DateKind.BestBefore, 28, null, 2, StorageLocation.Fridge)),
        (@"cereal|corn ?flakes|granola|muesli|bran flakes|weetabix|shreddies|cheerios|rice krispies|porridge|\boats\b",
            new("Cereal", DateKind.BestBefore, 180, 56, 60, StorageLocation.Cupboard)),
        (@"pasta|spaghetti|penne|fusilli|noodle|\brice\b|couscous|quinoa|lentil|flour|sugar|dried",
            new("Dry goods", DateKind.BestBefore, 365, 180, 180, StorageLocation.Cupboard)),
        (@"\btin(ned)?\b|\bcan(ned)?\b|baked beans|chopped tomatoes|soup|tuna|sweetcorn|chickpeas|kidney beans",
            new("Tins", DateKind.BestBefore, 730, 2, 365, StorageLocation.Cupboard)),
        (@"bread|loaf|rolls|bagel|wrap|tortilla|pitta|crumpet|muffin|brioche",
            new("Bread", DateKind.BestBefore, 5, null, 2, StorageLocation.Cupboard)),
        (@"biscuit|cookie|crisps|crackers|chocolate|sweets|cake bar|snack bar|popcorn|nuts",
            new("Snacks", DateKind.BestBefore, 120, 14, 30, StorageLocation.Cupboard)),
        (@"jam|marmalade|honey|peanut butter|ketchup|brown sauce|mustard|soy sauce|vinegar|pickle|chutney|syrup",
            new("Jars & sauces", DateKind.BestBefore, 365, 90, 90, StorageLocation.Cupboard)),
        (@"mayo",
            new("Mayonnaise", DateKind.BestBefore, 180, 60, 14, StorageLocation.Cupboard)),
        (@"coffee|\btea\b|teabags|cocoa|hot chocolate",
            new("Coffee & tea", DateKind.BestBefore, 365, 60, 180, StorageLocation.Cupboard)),
        (@"butter|margarine|spread",
            new("Butter", DateKind.BestBefore, 60, 30, 14, StorageLocation.Fridge)),
        (@"banana", new("Bananas", DateKind.BestBefore, 5, null, 2, StorageLocation.Cupboard)),
        (@"potato|onion|garlic|squash|pumpkin|sweet potato",
            new("Roots", DateKind.BestBefore, 21, null, 7, StorageLocation.Cupboard)),
        (@"apple|orange|lemon|lime|pear|carrot|cabbage|parsnip|swede|turnip",
            new("Hardy fruit & veg", DateKind.BestBefore, 14, null, 5, StorageLocation.Fridge)),
        (@"lettuce|salad|spinach|rocket|berries|strawberr|raspberr|blueberr|grapes|mushroom|tomato|cucumber|pepper|courgette|broccoli|herbs",
            new("Delicate fruit & veg", DateKind.BestBefore, 5, null, 1, StorageLocation.Fridge)),
        (@"frozen|ice cream|peas",
            new("Frozen", DateKind.BestBefore, 180, null, 60, StorageLocation.Freezer)),
        // Hard dates — no grace.
        (@"milk|cream|yog(h)?urt|cheese|ham|bacon|sausage|chicken|beef|pork|lamb|mince|turkey|fish|salmon|prawn|meat|pate|hummus|houmous|juice|ready meal|sandwich|coleslaw",
            new("Chilled", DateKind.UseBy, 7, 3, 0, StorageLocation.Fridge)),
    };

    /// <summary>LifeHub's guess for a food name (null when nothing matches).</summary>
    public static FreshProfile? ProfileFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.ToLowerInvariant();
        // Hard-date words win over generic ones ("chicken soup" in the fridge is chilled, not a tin) only for fresh items.
        foreach (var (pattern, profile) in Profiles)
            if (Regex.IsMatch(n, pattern)) return profile;
        return null;
    }

    public static DateKind KindFor(string name, StapleItem? staple) =>
        staple?.DateKind ?? ProfileFor(name)?.Kind ?? DateKind.UseBy;

    public static int GraceFor(PantryItem item, StapleItem? staple)
    {
        if (item.DateKind == DateKind.UseBy) return 0;
        return staple?.GraceDays ?? ProfileFor(item.Name)?.GraceDays ?? 3;
    }

    public static int? OpenedDaysFor(PantryItem item, StapleItem? staple) =>
        staple?.OpenedLifeDays ?? ProfileFor(item.Name)?.OpenedDays;

    public static FreshInfo Of(PantryItem item, StapleItem? staple, DateOnly today, int warnDays)
    {
        var date = item.UseBy;
        string? openedNote = null;
        // Once opened, some things go stale sooner (cereal, crisps, jars).
        if (item.OpenedOn is { } opened && OpenedDaysFor(item, staple) is { } od && item.Location != StorageLocation.Freezer)
        {
            var openedLimit = opened.AddDays(od);
            if (date is null || openedLimit < date)
            {
                date = openedLimit;
                openedNote = $"Opened {today.DayNumber - opened.DayNumber} days ago — usually best within {od} days of opening. ";
            }
        }
        if (date is not { } d) return new FreshInfo(FreshState.NoDate, "no date", "", 0, null, null);

        var days = d.DayNumber - today.DayNumber;
        var start = item.AddedOn == default ? d.AddDays(-14) : item.AddedOn;
        var approx = item.DateEstimated ? "~" : "";
        var lifespan = Math.Max(1, d.DayNumber - start.DayNumber);

        if (item.DateKind == DateKind.UseBy || item.Location == StorageLocation.Freezer)
        {
            var pos = Math.Clamp((today.DayNumber - start.DayNumber) / (double)lifespan, 0, 1);
            if (days < 0) return new FreshInfo(FreshState.Expired, days == -1 ? "expired yesterday" : $"expired {-days}d ago", "Use-by date has passed", 1, d, d);
            if (days <= warnDays)
                return new FreshInfo(FreshState.UseSoon, days == 0 ? "use today" : days == 1 ? "use by tomorrow" : $"use within {approx}{days} days", "Use by", pos, d, d);
            return new FreshInfo(FreshState.Fresh, $"{approx}{days} days", "Use by", pos, d, d);
        }

        // Best before: a gradient.
        var grace = GraceFor(item, staple);
        var checkBy = d.AddDays(grace);
        var full = Math.Max(1, checkBy.DayNumber - start.DayNumber);
        var position = Math.Clamp((today.DayNumber - start.DayNumber) / (double)full, 0, 1);
        if (days > warnDays) return new FreshInfo(FreshState.Fresh, Weeks(days, approx), openedNote + "Best before", position, d, checkBy);
        if (days >= 0)
            return new FreshInfo(FreshState.UseSoon, days == 0 ? "best before today" : $"best before {approx}{days}d", openedNote + "Still at its best — use soon", position, d, checkBy);
        if (today <= checkBy)
            return new FreshInfo(FreshState.PastBest, $"past best · {-days}d", openedNote + $"Usually still fine for about {grace} days after the date — check it looks and smells right", position, d, checkBy);
        return new FreshInfo(FreshState.CheckFirst, "check before using", openedNote + $"{-days} days past best before — check carefully or bin it", 1, d, checkBy);
    }

    private static string Weeks(int days, string approx) => days switch
    {
        >= 60 => $"{approx}{days / 30} months",
        >= 21 => $"{approx}{days / 7} weeks",
        _ => $"{approx}{days} days"
    };

    /// <summary>Estimated date for something without a readable date (from the staple, product or built-in profile).</summary>
    public static (DateOnly? Date, DateKind Kind) Estimate(string name, StapleItem? staple, int? productShelfDays, DateOnly from, StorageLocation location)
    {
        var profile = ProfileFor(name);
        var kind = staple?.DateKind ?? profile?.Kind ?? DateKind.UseBy;
        int? days = productShelfDays ?? staple?.ShelfLifeDays ?? profile?.ShelfDays;
        if (location == StorageLocation.Freezer) return (null, DateKind.BestBefore);
        return (days is { } n ? from.AddDays(n) : null, kind);
    }
}
