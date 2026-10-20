namespace LifeHub.Services;

public sealed class RenewalService
{
    private readonly IRepository<Renewal> _repo;
    private readonly Clock _clock;

    public RenewalService(IRepository<Renewal> repo, Clock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public IRepository<Renewal> Repo => _repo;

    public IReadOnlyList<Renewal> Active() => _repo.Where(r => r.Status != RenewalStatus.Cancelled);

    public IReadOnlyList<Renewal> DueWithin(int days)
    {
        var today = _clock.Today;
        return _repo.Where(r => r.Status != RenewalStatus.Cancelled && r.DaysUntil(today) <= days)
            .OrderBy(r => r.NextRenewal).ToList();
    }

    public decimal MonthlyTotal() => Active().Sum(r => r.MonthlyCost);

    /// <summary>Saves and records a price point whenever the cost changes (so increases are tracked automatically).</summary>
    public async Task SaveAsync(Renewal r, string? priceNote = null)
    {
        var existing = _repo.Get(r.Id);
        var last = r.PriceHistory.OrderBy(p => p.Date).LastOrDefault();
        if (existing is null && r.PriceHistory.Count == 0)
            r.PriceHistory.Add(new PricePoint { Date = _clock.Today, Cost = r.Cost, Note = "Initial" });
        else if (last is null || last.Cost != r.Cost)
            r.PriceHistory.Add(new PricePoint { Date = _clock.Today, Cost = r.Cost, Note = priceNote ?? "Price changed" });
        await _repo.UpsertAsync(r);
    }

    /// <summary>Marks the current period as paid/renewed and moves the date on.</summary>
    public async Task MarkRenewedAsync(Guid id, decimal? newCost = null)
    {
        var r = _repo.Get(id);
        if (r is null) return;
        if (newCost is { } c && c != r.Cost) r.Cost = c;
        if (r.Cycle == BillingCycle.OneOff) r.Status = RenewalStatus.Cancelled;
        else
        {
            r.NextRenewal = r.Advance(r.NextRenewal);
            while (r.NextRenewal < _clock.Today) r.NextRenewal = r.Advance(r.NextRenewal);
        }
        if (r.Status == RenewalStatus.Trial) r.Status = RenewalStatus.Active;
        r.LastRemindedFor = null;
        await SaveAsync(r, "Renewed");
    }

    /// <summary>Auto-renewing items whose date has passed roll forward; "cancelling" ones become cancelled.</summary>
    public async Task<int> RollForwardAsync()
    {
        var today = _clock.Today;
        var changed = new List<Renewal>();
        foreach (var r in _repo.Where(r => r.Status != RenewalStatus.Cancelled && r.NextRenewal < today))
        {
            if (r.Status == RenewalStatus.Cancelling || r.Cycle == BillingCycle.OneOff) { r.Status = RenewalStatus.Cancelled; changed.Add(r); continue; }
            if (!r.AutoRenew) continue;
            while (r.NextRenewal < today) r.NextRenewal = r.Advance(r.NextRenewal);
            r.LastRemindedFor = null;
            changed.Add(r);
        }
        if (changed.Count > 0) await _repo.UpsertManyAsync(changed);
        return changed.Count;
    }

    /// <summary>Renewals that went up in price in the last N days.</summary>
    public IReadOnlyList<Renewal> RecentIncreases(int days = 90)
    {
        var since = _clock.Today.AddDays(-days);
        return _repo.Where(r => r.PriceHistory.Count >= 2 && r.LastChangePercent > 0 &&
                                r.PriceHistory.Max(p => p.Date) >= since).ToList();
    }
}
