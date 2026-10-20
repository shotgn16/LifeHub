namespace LifeHub.Services;

public sealed record StapleStatus(StapleItem Staple, decimal InStock, double? AvgDaysBetweenBuys, DateOnly? LastBought, DateOnly? PredictedRunOut)
{
    public bool Low => InStock < Staple.MinQuantity;
}

public sealed class KitchenService
{
    private readonly IRepository<PantryItem> _pantry;
    private readonly IRepository<StapleItem> _staples;
    private readonly IRepository<ShoppingItem> _shopping;
    private readonly IRepository<FoodEvent> _events;
    private readonly IRepository<ProductInfo> _products;
    private readonly SettingsService _settings;
    private readonly Clock _clock;

    public KitchenService(IRepository<PantryItem> pantry, IRepository<StapleItem> staples, IRepository<ShoppingItem> shopping,
        IRepository<FoodEvent> events, IRepository<ProductInfo> products, SettingsService settings, Clock clock)
    {
        _pantry = pantry; _staples = staples; _shopping = shopping; _events = events; _products = products; _settings = settings; _clock = clock;
    }

    public IRepository<PantryItem> Pantry => _pantry;
    public IRepository<StapleItem> Staples => _staples;
    public IRepository<ShoppingItem> Shopping => _shopping;
    public IRepository<FoodEvent> Events => _events;
    public IRepository<ProductInfo> Products => _products;

    private StapleItem? FindStaple(string name) => _staples.All().FirstOrDefault(s => s.Matches(name));

    // ---------------- Inventory ----------------

    public IReadOnlyList<PantryItem> ExpiringWithin(int days)
    {
        var today = _clock.Today;
        return _pantry.Where(p => p.Location != StorageLocation.Freezer && p.UseBy is { } d && d.DayNumber - today.DayNumber <= days)
            .Concat(_pantry.Where(p => p.Location == StorageLocation.Freezer && p.UseBy is { } d && d.DayNumber - today.DayNumber <= 7))
            .OrderBy(p => p.UseBy).ToList();
    }

    public async Task<PantryItem> AddToPantryAsync(PantryItem item, bool logPurchase = true)
    {
        item.Name = item.Name.Trim();
        if (item.AddedOn == default) item.AddedOn = _clock.Today;
        var staple = item.StapleId is { } sid ? _staples.Get(sid) : FindStaple(item.Name);
        if (staple is not null)
        {
            item.StapleId = staple.Id;
            if (item.UseBy is null && staple.ShelfLifeDays is { } days) item.UseBy = item.AddedOn.AddDays(days);
            if (string.IsNullOrEmpty(item.Unit)) item.Unit = staple.Unit;
            if (item.Category == "Other") item.Category = staple.Category;
        }
        if (item.Location == StorageLocation.Freezer && item.FrozenOn is null)
        {
            item.FrozenOn = item.AddedOn;
            item.UseBy ??= item.AddedOn.AddDays(_settings.Current.General.FreezerShelfLifeDays);
        }
        await _pantry.UpsertAsync(item);
        if (logPurchase) await LogAsync(item.Name, item.StapleId, FoodEventKind.Purchased, item.Quantity);
        return item;
    }

    public Task SavePantryAsync(PantryItem item) => _pantry.UpsertAsync(item);

    /// <summary>Uses some (or all) of an item. Removes it when nothing is left.</summary>
    public async Task UseAsync(Guid id, decimal? amount = null, bool wasted = false)
    {
        var item = _pantry.Get(id);
        if (item is null) return;
        var used = amount ?? item.Quantity;
        item.Quantity -= used;
        if (item.Quantity <= 0) await _pantry.DeleteAsync(id);
        else
        {
            item.OpenedOn ??= _clock.Today;
            await _pantry.UpsertAsync(item);
        }
        await LogAsync(item.Name, item.StapleId, wasted ? FoodEventKind.Wasted : FoodEventKind.Used, used);
        await RefreshAutoShoppingAsync();
    }

    public async Task FreezeAsync(Guid id)
    {
        var item = _pantry.Get(id);
        if (item is null || item.Location == StorageLocation.Freezer) return;
        item.Location = StorageLocation.Freezer;
        item.FrozenOn = _clock.Today;
        item.UseBy = _clock.Today.AddDays(_settings.Current.General.FreezerShelfLifeDays);
        await _pantry.UpsertAsync(item);
        await LogAsync(item.Name, item.StapleId, FoodEventKind.Frozen, item.Quantity);
    }

    public async Task DefrostAsync(Guid id)
    {
        var item = _pantry.Get(id);
        if (item is null || item.Location != StorageLocation.Freezer) return;
        item.Location = StorageLocation.Fridge;
        item.UseBy = _clock.Today.AddDays(_settings.Current.General.DefrostedShelfLifeDays);
        await _pantry.UpsertAsync(item);
        await LogAsync(item.Name, item.StapleId, FoodEventKind.Defrosted, item.Quantity);
    }

    private Task LogAsync(string name, Guid? stapleId, FoodEventKind kind, decimal qty) =>
        _events.UpsertAsync(new FoodEvent { Name = name, StapleId = stapleId, Kind = kind, Quantity = qty, Date = _clock.Today });

    private Task RemoveLastEventAsync(string name, FoodEventKind kind)
    {
        var last = _events.Where(e => e.Kind == kind && e.Name == name && e.Date == _clock.Today)
            .OrderByDescending(e => e.CreatedUtc).FirstOrDefault();
        return last is null ? Task.CompletedTask : _events.DeleteAsync(last.Id);
    }

    /// <summary>Reverses AddToPantryAsync (used by scanner/AI undo).</summary>
    public async Task UndoAddAsync(PantryItem added)
    {
        await _pantry.DeleteAsync(added.Id);
        await RemoveLastEventAsync(added.Name, FoodEventKind.Purchased);
    }

    /// <summary>Restores an item exactly as it was before UseAsync.</summary>
    public async Task UndoUseAsync(PantryItem before)
    {
        await _pantry.UpsertAsync(before);
        await RemoveLastEventAsync(before.Name, FoodEventKind.Used);
    }

    // ---------------- Staples & predictions ----------------

    public decimal StockOf(StapleItem staple) =>
        _pantry.Where(p => p.StapleId == staple.Id || staple.Matches(p.Name)).Sum(p => p.Quantity);

    public StapleStatus StatusOf(StapleItem staple)
    {
        var buys = _events.Where(e => e.Kind == FoodEventKind.Purchased && (e.StapleId == staple.Id || staple.Matches(e.Name)))
            .Select(e => e.Date).Distinct().OrderBy(d => d).ToList();
        double? avg = null;
        DateOnly? runOut = null;
        if (buys.Count >= 2)
        {
            // Use the last 6 intervals so habits can change over time.
            var intervals = buys.Zip(buys.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Where(i => i > 0).TakeLast(6).ToList();
            if (intervals.Count > 0)
            {
                avg = intervals.Average();
                runOut = buys[^1].AddDays((int)Math.Round(avg.Value));
            }
        }
        return new StapleStatus(staple, StockOf(staple), avg, buys.Count > 0 ? buys[^1] : null, runOut);
    }

    public IReadOnlyList<StapleStatus> StapleStatuses() => _staples.All().OrderBy(s => s.Name).Select(StatusOf).ToList();

    /// <summary>
    /// Adds staples that are low, predicted to run out within 2 days, or only have expired stock.
    /// Never duplicates something already on the list. Returns number added.
    /// </summary>
    public async Task<int> RefreshAutoShoppingAsync()
    {
        var today = _clock.Today;
        var list = _shopping.Where(s => !s.Checked);
        var adds = new List<ShoppingItem>();

        foreach (var st in StapleStatuses())
        {
            if (list.Any(l => l.StapleId == st.Staple.Id || st.Staple.Matches(l.Name))) continue;

            string? reason = null;
            var freshStock = _pantry.Where(p => (p.StapleId == st.Staple.Id || st.Staple.Matches(p.Name)) &&
                                                (p.UseBy is null || p.UseBy >= today)).Sum(p => p.Quantity);
            if (st.InStock < st.Staple.MinQuantity) reason = "Low stock";
            else if (freshStock < st.Staple.MinQuantity) reason = "Expired";
            else if (st.Staple.PredictFromHistory && st.PredictedRunOut is { } r && r.DayNumber - today.DayNumber <= 2) reason = "Predicted";

            if (reason is null) continue;
            adds.Add(new ShoppingItem
            {
                Name = st.Staple.Name, Quantity = st.Staple.BuyQuantity, Unit = st.Staple.Unit,
                Category = st.Staple.Category, Reason = reason, StapleId = st.Staple.Id
            });
        }
        if (adds.Count > 0) await _shopping.UpsertManyAsync(adds);
        return adds.Count;
    }

    // ---------------- Shopping list ----------------

    public async Task<ShoppingItem> AddToShoppingAsync(string name, decimal qty = 1, string unit = "", string reason = "Manual", string? category = null)
    {
        name = name.Trim();
        var existing = _shopping.Where(s => !s.Checked && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (existing is not null)
        {
            existing.Quantity += qty;
            await _shopping.UpsertAsync(existing);
            return existing;
        }
        var staple = FindStaple(name);
        var item = new ShoppingItem
        {
            Name = name, Quantity = qty, Unit = string.IsNullOrEmpty(unit) ? staple?.Unit ?? "" : unit,
            Category = category ?? staple?.Category ?? "Other", Reason = reason, StapleId = staple?.Id
        };
        await _shopping.UpsertAsync(item);
        return item;
    }

    public async Task ToggleAsync(Guid id)
    {
        var item = _shopping.Get(id);
        if (item is null) return;
        item.Checked = !item.Checked;
        await _shopping.UpsertAsync(item);
    }

    /// <summary>Moves every ticked item into the pantry (with sensible locations and use-by dates) and clears them from the list.</summary>
    public async Task<int> PutAwayCheckedAsync()
    {
        var done = _shopping.Where(s => s.Checked);
        foreach (var s in done)
        {
            var staple = s.StapleId is { } id ? _staples.Get(id) : FindStaple(s.Name);
            var product = string.IsNullOrEmpty(s.Barcode) ? null : _products.Where(p => p.Barcode == s.Barcode).FirstOrDefault();
            await AddToPantryAsync(new PantryItem
            {
                Name = s.Name, Quantity = s.Quantity <= 0 ? 1 : s.Quantity, Unit = s.Unit, Category = s.Category,
                Location = product?.DefaultLocation ?? staple?.DefaultLocation ?? GuessLocation(s.Category),
                StapleId = product?.StapleId ?? staple?.Id, Barcode = s.Barcode,
                UseBy = product is { ShelfLifeDays: { } days, DefaultLocation: not StorageLocation.Freezer } ? _clock.Today.AddDays(days) : null
            });
        }
        await _shopping.DeleteManyAsync(done.Select(d => d.Id));
        return done.Count;
    }

    public static StorageLocation GuessLocation(string category) => category switch
    {
        "Dairy" or "Meat & Fish" or "Fruit & Veg" => StorageLocation.Fridge,
        "Frozen" => StorageLocation.Freezer,
        _ => StorageLocation.Cupboard
    };

    public (int Purchased, int Used, int Wasted) StatsSince(DateOnly since)
    {
        var e = _events.Where(x => x.Date >= since);
        return (e.Count(x => x.Kind == FoodEventKind.Purchased), e.Count(x => x.Kind == FoodEventKind.Used), e.Count(x => x.Kind == FoodEventKind.Wasted));
    }
}
