using System.Text.Json;

namespace LifeHub.Services;

public enum ScanMode { Add, Use, Shopping }

/// <summary>Result of a scan action. <see cref="Undo"/> reverses it (null when there's nothing to undo).</summary>
public sealed record ScanOutcome(
    bool Success,
    string Message,
    ProductInfo? Product = null,
    Func<Task>? Undo = null,
    bool NeedsDetails = false,
    bool NotInStock = false);

/// <summary>
/// Barcode → product → kitchen action. Products are remembered locally; unknown barcodes are
/// looked up on Open Food Facts (only the number is sent) and can be renamed/linked to a staple.
/// </summary>
public sealed class BarcodeService
{
    private readonly IRepository<ProductInfo> _products;
    private readonly KitchenService _kitchen;
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly Clock _clock;
    private readonly ILogger<BarcodeService> _log;

    public BarcodeService(IRepository<ProductInfo> products, KitchenService kitchen, SettingsService settings,
        IHttpClientFactory http, Clock clock, ILogger<BarcodeService> log)
    {
        _products = products; _kitchen = kitchen; _settings = settings; _http = http; _clock = clock; _log = log;
    }

    public IRepository<ProductInfo> Products => _products;

    /// <summary>Digits only; 12-digit UPC-A is stored as its 13-digit EAN form so both scan the same.</summary>
    public static string Normalize(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.All(char.IsDigit) && t.Length == 12) return "0" + t;
        return t;
    }

    public ProductInfo? Find(string barcode)
    {
        var code = Normalize(barcode);
        return _products.Where(p => p.Barcode == code).FirstOrDefault();
    }

    /// <summary>Local memory first, then Open Food Facts. Returns null if nobody knows the barcode.</summary>
    public async Task<ProductInfo?> LookupAsync(string barcode, CancellationToken ct = default)
    {
        var code = Normalize(barcode);
        if (string.IsNullOrEmpty(code)) return null;
        var local = Find(code);
        if (local is not null) return local;

        var g = _settings.Current.General;
        if (!g.BarcodeLookupOnline || !code.All(char.IsDigit)) return null;
        try
        {
            var p = await LookupOpenFoodFactsAsync(code, g.BarcodeLookupUrl, ct);
            if (p is null) return null;
            await _products.UpsertAsync(p);
            return p;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning(ex, "Barcode lookup failed for {Code}", code);
            return null;
        }
    }

    private async Task<ProductInfo?> LookupOpenFoodFactsAsync(string code, string baseUrl, CancellationToken ct)
    {
        var c = _http.CreateClient("off");
        c.Timeout = TimeSpan.FromSeconds(8);
        // Open Food Facts asks API users to identify their app.
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LifeHub/1.1 (self-hosted household app)");
        var url = $"{baseUrl.TrimEnd('/')}/api/v2/product/{code}.json?fields=product_name,product_name_en,generic_name,brands,quantity,categories_tags";
        using var r = await c.GetAsync(url, ct);
        if (r.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        r.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (!root.TryGetProperty("status", out var st) || st.ValueKind != JsonValueKind.Number || st.GetInt32() != 1) return null;
        if (!root.TryGetProperty("product", out var prod)) return null;

        string S(string name) => prod.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
        var name = FirstNonEmpty(S("product_name_en"), S("product_name"), S("generic_name"));
        if (string.IsNullOrWhiteSpace(name)) return null;
        var brand = S("brands").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var tags = prod.TryGetProperty("categories_tags", out var ct2) && ct2.ValueKind == JsonValueKind.Array
            ? ct2.EnumerateArray().Select(t => t.GetString() ?? "").ToList() : new List<string>();

        var staple = GuessStaple(name);
        // The product's own categories are the best signal; fall back to the linked staple.
        var category = GuessCategory(tags);
        if (category == "Other" && staple is not null) category = staple.Category;
        var location = KitchenService.GuessLocation(category);
        if (location == StorageLocation.Cupboard && staple is not null) location = staple.DefaultLocation;
        return new ProductInfo
        {
            Barcode = code, Name = name, Brand = brand, PackSize = S("quantity"),
            Category = category, DefaultLocation = location,
            ShelfLifeDays = staple?.ShelfLifeDays,
            StapleId = staple?.Id, Source = "openfoodfacts", Confirmed = false
        };
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    /// <summary>Maps Open Food Facts category tags onto the household's food categories.</summary>
    public string GuessCategory(IEnumerable<string> tags)
    {
        var t = string.Join(' ', tags).ToLowerInvariant();
        string? guess =
            t.Contains("frozen") ? "Frozen" :
            Any(t, "dairies", "milks", "cheeses", "yogurts", "butters", "creams", "eggs") ? "Dairy" :
            Any(t, "meats", "poultry", "fishes", "seafood", "sausages", "hams") ? "Meat & Fish" :
            Any(t, "fruits", "vegetables", "salads", "fresh-produce") ? "Fruit & Veg" :
            Any(t, "breads", "bakery", "pastries", "cakes") ? "Bakery" :
            Any(t, "beverages", "drinks", "waters", "coffees", "teas", "juices", "sodas", "beers", "wines") ? "Drinks" :
            Any(t, "snacks", "crisps", "chips", "biscuits", "chocolates", "confectioneries", "sweets") ? "Snacks" :
            Any(t, "canned", "jams", "sauces", "condiments", "spreads", "soups", "pickles") ? "Tins & Jars" :
            Any(t, "pastas", "rices", "cereals", "flours", "legumes", "breakfast", "grains", "noodles") ? "Dry Goods" :
            Any(t, "cleaning", "household", "hygiene", "toilet") ? "Household" : null;
        var cats = _settings.Current.General.FoodCategories;
        return guess is not null && cats.Contains(guess) ? guess : "Other";
    }

    private static bool Any(string text, params string[] words) => words.Any(w => text.Contains(w));

    /// <summary>A staple whose name (or alias) appears as a word in the product name, longest match first.</summary>
    public StapleItem? GuessStaple(string productName)
    {
        var words = System.Text.RegularExpressions.Regex.Split(productName.ToLowerInvariant(), @"[^\p{L}\p{N}]+");
        var text = " " + string.Join(' ', words) + " ";
        return _kitchen.Staples.All()
            .SelectMany(s => s.Aliases.Prepend(s.Name).Select(n => (Staple: s, Name: n.Trim().ToLowerInvariant())))
            .Where(x => x.Name.Length > 1 && text.Contains(" " + x.Name + " "))
            .OrderByDescending(x => x.Name.Length)
            .Select(x => x.Staple).FirstOrDefault();
    }

    public ProductInfo NewUnknown(string barcode) => new()
    {
        Barcode = Normalize(barcode), Source = "manual", Category = "Other", DefaultLocation = StorageLocation.Cupboard
    };

    /// <summary>Saves product details the user confirmed.</summary>
    public async Task SaveAsync(ProductInfo p)
    {
        p.Name = p.Name.Trim();
        p.Confirmed = true;
        await _products.UpsertAsync(p);
    }

    private async Task TouchAsync(ProductInfo p)
    {
        p.ScanCount++;
        p.LastScannedUtc = DateTime.UtcNow;
        await _products.UpsertAsync(p);
    }

    /// <summary>Default use-by for a product (learned shelf life, else staple), never for freezer items.</summary>
    public DateOnly? DefaultUseBy(ProductInfo p, StorageLocation location)
    {
        if (location == StorageLocation.Freezer) return null;
        var days = p.ShelfLifeDays ?? (p.StapleId is { } sid ? _kitchen.Staples.Get(sid)?.ShelfLifeDays : null);
        return days is { } d ? _clock.Today.AddDays(d) : null;
    }

    // ------------------------------------------------------------------ actions

    public async Task<ScanOutcome> AddAsync(ProductInfo p, decimal quantity, StorageLocation location, DateOnly? useBy, bool learn)
    {
        if (learn)
        {
            p.DefaultLocation = location;
            if (useBy is { } u && location != StorageLocation.Freezer && u > _clock.Today)
                p.ShelfLifeDays = u.DayNumber - _clock.Today.DayNumber;
        }
        await TouchAsync(p);
        var item = await _kitchen.AddToPantryAsync(new PantryItem
        {
            Name = p.Name, Quantity = quantity <= 0 ? 1 : quantity, Category = p.Category, Location = location,
            UseBy = useBy, StapleId = p.StapleId, Barcode = p.Barcode
        });
        // Bought it → tick it off the shopping list.
        var onList = _kitchen.Shopping.Where(s => !s.Checked && (s.Barcode == p.Barcode || s.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase) ||
                                                                 (p.StapleId != null && s.StapleId == p.StapleId))).FirstOrDefault();
        if (onList is not null) await _kitchen.Shopping.DeleteAsync(onList.Id);

        var where = location.ToString().ToLowerInvariant();
        var useByText = item.UseBy is { } ub ? $", use by {ub:ddd d MMM}" : "";
        return new ScanOutcome(true, $"Added {p.Name} to the {where}{useByText}", p, async () =>
        {
            await _kitchen.UndoAddAsync(item);
            if (onList is not null) await _kitchen.Shopping.UpsertAsync(onList);
        });
    }

    /// <summary>Uses one of this product: the same barcode first, else the same staple, else the same name — soonest use-by first.</summary>
    public async Task<ScanOutcome> UseAsync(ProductInfo p)
    {
        await TouchAsync(p);
        var stock = _kitchen.Pantry.Where(i => i.Barcode == p.Barcode);
        if (stock.Count == 0 && p.StapleId is { } sid) stock = _kitchen.Pantry.Where(i => i.StapleId == sid);
        if (stock.Count == 0) stock = _kitchen.Pantry.Where(i => i.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase));
        var item = stock
            .OrderBy(i => i.Location == StorageLocation.Freezer)
            .ThenBy(i => i.UseBy ?? DateOnly.MaxValue)
            .ThenBy(i => i.AddedOn)
            .FirstOrDefault();
        if (item is null) return new ScanOutcome(false, $"{p.Name} isn't in your kitchen stock", p, NotInStock: true);

        var before = Json.Clone(item);
        await _kitchen.UseAsync(item.Id, item.Quantity > 1 ? 1 : null);
        var left = _kitchen.Pantry.Get(item.Id)?.Quantity ?? 0;
        var msg = left > 0 ? $"Used 1 {p.Name} — {Fmt(left)} left" : $"Used the last {p.Name}";
        if (left <= 0 && p.StapleId is not null && _kitchen.Shopping.Where(s => !s.Checked && s.StapleId == p.StapleId).Count > 0)
            msg += " (added to shopping list)";
        return new ScanOutcome(true, msg, p, () => _kitchen.UndoUseAsync(before));
    }

    public async Task<ScanOutcome> AddToShoppingAsync(ProductInfo p, decimal quantity = 1)
    {
        await TouchAsync(p);
        var existing = _kitchen.Shopping.Where(s => !s.Checked && s.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        var item = await _kitchen.AddToShoppingAsync(p.Name, quantity, "", "Scanned", p.Category);
        if (item.Barcode != p.Barcode || (p.StapleId is not null && item.StapleId is null))
        {
            item.Barcode = p.Barcode;
            item.StapleId ??= p.StapleId;
            await _kitchen.Shopping.UpsertAsync(item);
        }
        return new ScanOutcome(true, existing is null ? $"{p.Name} added to the shopping list" : $"{p.Name} — now {Fmt(item.Quantity)} on the list", p, async () =>
        {
            var current = _kitchen.Shopping.Get(item.Id);
            if (current is null) return;
            if (existing is null) await _kitchen.Shopping.DeleteAsync(item.Id);
            else { current.Quantity = existing.Quantity; await _kitchen.Shopping.UpsertAsync(current); }
        });
    }

    /// <summary>
    /// One-shot handling for instant scans and the REST API. Returns NeedsDetails when the
    /// product is unknown, or (when <paramref name="requireConfirmed"/>) not yet confirmed for adding.
    /// </summary>
    public async Task<ScanOutcome> HandleAsync(string barcode, ScanMode mode, bool requireConfirmed, decimal quantity = 1, CancellationToken ct = default)
    {
        var p = await LookupAsync(barcode, ct);
        if (p is null) return new ScanOutcome(false, "Unknown barcode — what is it?", NewUnknown(barcode), NeedsDetails: true);
        if (requireConfirmed && !p.Confirmed && mode == ScanMode.Add)
            return new ScanOutcome(false, "Check the details", p, NeedsDetails: true);
        return mode switch
        {
            ScanMode.Use => await UseAsync(p),
            ScanMode.Shopping => await AddToShoppingAsync(p, quantity),
            _ => await AddAsync(p, quantity, p.DefaultLocation, DefaultUseBy(p, p.DefaultLocation), learn: false)
        };
    }

    private static string Fmt(decimal q) => q == Math.Floor(q) ? q.ToString("0") : q.ToString("0.##");
}
