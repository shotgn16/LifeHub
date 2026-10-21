namespace LifeHub.Services;

/// <summary>A staple you might want to buy. Nothing is added to the list until you tap it.</summary>
public sealed record ShoppingSuggestion(StapleItem Staple, string Reason, decimal Quantity);

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

    public StapleItem? StapleOf(PantryItem p) => p.StapleId is { } id ? _staples.Get(id) : null;

    /// <summary>Where an item is in its life (use-by cliff edge, or best-before gradient).</summary>
    public FreshInfo Fresh(PantryItem p) => Freshness.Of(p, StapleOf(p), _clock.Today, _settings.Current.General.ExpiryWarningDays);

    /// <summary>
    /// Food to use up: use-by within <paramref name="days"/> or passed, best-before food that's due soon or past
    /// its best (soft dates fade, so they're only flagged when it matters), and freezer items within a week.
    /// </summary>
    public IReadOnlyList<PantryItem> ExpiringWithin(int days)
    {
        var today = _clock.Today;
        return _pantry.Where(p =>
            {
                if (p.Location == StorageLocation.Freezer) return p.UseBy is { } fd && fd.DayNumber - today.DayNumber <= 7;
                var f = Freshness.Of(p, StapleOf(p), today, days);
                return f.State is FreshState.UseSoon or FreshState.Expired or FreshState.CheckFirst or FreshState.PastBest;
            })
            .OrderBy(p => Freshness.Of(p, StapleOf(p), today, days).EffectiveDate ?? DateOnly.MaxValue).ToList();
    }

    /// <summary>Only the items worth a reminder: use-by soon/passed and best-before food that now needs checking.</summary>
    public IReadOnlyList<PantryItem> NeedsAttention(int days)
    {
        var today = _clock.Today;
        return ExpiringWithin(days).Where(p => p.Location == StorageLocation.Freezer || Freshness.Of(p, StapleOf(p), today, days).NeedsAttention).ToList();
    }

    /// <param name="kind">Use-by or best-before; null = LifeHub's guess from the staple or food type.</param>
    public async Task<PantryItem> AddToPantryAsync(PantryItem item, bool logPurchase = true, DateKind? kind = null)
    {
        item.Name = item.Name.Trim();
        if (item.AddedOn == default) item.AddedOn = _clock.Today;
        var staple = item.StapleId is { } sid ? _staples.Get(sid) : FindStaple(item.Name);
        item.DateKind = kind ?? Freshness.KindFor(item.Name, staple);
        if (staple is not null)
        {
            item.StapleId = staple.Id;
            if (string.IsNullOrEmpty(item.Unit)) item.Unit = staple.Unit;
            if (item.Category == "Other") item.Category = staple.Category;
        }
        if (item.UseBy is null && item.Location != StorageLocation.Freezer)
        {
            var (est, estKind) = Freshness.Estimate(item.Name, staple, null, item.AddedOn, item.Location);
            if (est is not null)
            {
                item.UseBy = est;
                item.DateEstimated = true;
                if (kind is null) item.DateKind = estKind;
            }
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
    /// Staples that are low, only have out-of-date stock, or that your buying history says will run out
    /// within 2 days. These are only suggestions: the shopping list only changes when you add one
    /// (or add items yourself, scan them, or use the linked Home Assistant list).
    /// </summary>
    public IReadOnlyList<ShoppingSuggestion> ShoppingSuggestions()
    {
        var today = _clock.Today;
        var warn = _settings.Current.General.ExpiryWarningDays;
        var list = _shopping.Where(s => !s.Checked);
        var result = new List<ShoppingSuggestion>();
        foreach (var st in StapleStatuses())
        {
            if (list.Any(l => l.StapleId == st.Staple.Id || st.Staple.Matches(l.Name))) continue;
            var freshStock = _pantry.Where(p => (p.StapleId == st.Staple.Id || st.Staple.Matches(p.Name)) &&
                                                Freshness.Of(p, st.Staple, today, warn).State is not (FreshState.Expired or FreshState.CheckFirst)).Sum(p => p.Quantity);
            string? reason = null;
            if (st.InStock < st.Staple.MinQuantity) reason = st.InStock == 0 ? "None left" : "Running low";
            else if (freshStock < st.Staple.MinQuantity) reason = "Only old stock left";
            else if (st.Staple.PredictFromHistory && st.PredictedRunOut is { } r && r.DayNumber - today.DayNumber <= 2) reason = "Usually bought about now";
            if (reason is not null) result.Add(new ShoppingSuggestion(st.Staple, reason, st.Staple.BuyQuantity));
        }
        return result;
    }

    public Task<ShoppingItem> AddSuggestionAsync(ShoppingSuggestion s) =>
        AddToShoppingAsync(s.Staple.Name, s.Quantity <= 0 ? 1 : s.Quantity, s.Staple.Unit, "Manual", s.Staple.Category);

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
            var fromProduct = product is { ShelfLifeDays: { } days, DefaultLocation: not StorageLocation.Freezer } ? _clock.Today.AddDays(days) : (DateOnly?)null;
            await AddToPantryAsync(new PantryItem
            {
                Name = s.Name, Quantity = s.Quantity <= 0 ? 1 : s.Quantity, Unit = s.Unit, Category = s.Category,
                Location = product?.DefaultLocation ?? staple?.DefaultLocation ?? GuessLocation(s.Category, s.Name),
                StapleId = product?.StapleId ?? staple?.Id, Barcode = s.Barcode,
                UseBy = fromProduct, DateEstimated = fromProduct is not null
            });
        }
        await _shopping.DeleteManyAsync(done.Select(d => d.Id));
        return done.Count;
    }

    public static StorageLocation GuessLocation(string category, string? name = null) =>
        name is not null && Freshness.ProfileFor(name) is { } p && category is not ("Frozen") ? p.Location : category switch
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
