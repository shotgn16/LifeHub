namespace LifeHub.Domain;

public enum RenewalKind { Domain, Hosting, Subscription, Software, Streaming, Utility, Insurance, Membership, Service, Other }
public enum BillingCycle { Weekly, Monthly, Quarterly, SixMonthly, Yearly, TwoYearly, CustomDays, OneOff }
public enum RenewalStatus { Active, Trial, Cancelling, Cancelled }

public sealed class PricePoint
{
    public DateOnly Date { get; set; }
    public decimal Cost { get; set; }
    public string Note { get; set; } = "";
}

[Owned(OwnScope.Renewals)]
public sealed class Renewal : Entity
{
    public string Name { get; set; } = "";
    public RenewalKind Kind { get; set; } = RenewalKind.Subscription;
    public string Provider { get; set; } = "";
    public string Url { get; set; } = "";
    public string AccountLogin { get; set; } = "";
    public decimal Cost { get; set; }
    public string Currency { get; set; } = "GBP";
    public BillingCycle Cycle { get; set; } = BillingCycle.Monthly;
    public int CycleDays { get; set; } = 30;
    public DateOnly NextRenewal { get; set; }
    public bool AutoRenew { get; set; } = true;
    public string PaymentMethod { get; set; } = "";
    public int RemindDaysBefore { get; set; } = 14;
    public RenewalStatus Status { get; set; } = RenewalStatus.Active;
    public DateOnly? TrialEnds { get; set; }
    public decimal? UsageCurrent { get; set; }
    public decimal? UsageLimit { get; set; }
    public string UsageUnit { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<PricePoint> PriceHistory { get; set; } = new();
    public DateOnly? LastRemindedFor { get; set; }

    public DateOnly Advance(DateOnly from) => Cycle switch
    {
        BillingCycle.Weekly => from.AddDays(7),
        BillingCycle.Monthly => from.AddMonths(1),
        BillingCycle.Quarterly => from.AddMonths(3),
        BillingCycle.SixMonthly => from.AddMonths(6),
        BillingCycle.Yearly => from.AddYears(1),
        BillingCycle.TwoYearly => from.AddYears(2),
        BillingCycle.CustomDays => from.AddDays(Math.Max(1, CycleDays)),
        _ => from
    };

    /// <summary>Normalised monthly cost, for totals.</summary>
    public decimal MonthlyCost => Status is RenewalStatus.Cancelled ? 0 : Cycle switch
    {
        BillingCycle.Weekly => Cost * 52m / 12m,
        BillingCycle.Monthly => Cost,
        BillingCycle.Quarterly => Cost / 3m,
        BillingCycle.SixMonthly => Cost / 6m,
        BillingCycle.Yearly => Cost / 12m,
        BillingCycle.TwoYearly => Cost / 24m,
        BillingCycle.CustomDays => Cost * 30.4375m / Math.Max(1, CycleDays),
        _ => 0
    };

    public decimal YearlyCost => MonthlyCost * 12m;

    public int DaysUntil(DateOnly today) => NextRenewal.DayNumber - today.DayNumber;

    public double? UsagePercent => UsageLimit is > 0 && UsageCurrent is { } u ? (double)(u / UsageLimit.Value * 100m) : null;

    /// <summary>Percentage change between the last two recorded prices (null when there is no history).</summary>
    public double? LastChangePercent
    {
        get
        {
            var h = PriceHistory.OrderBy(p => p.Date).ToList();
            if (h.Count < 2 || h[^2].Cost == 0) return null;
            return (double)((h[^1].Cost - h[^2].Cost) / h[^2].Cost * 100m);
        }
    }

    /// <summary>Change since the first recorded price.</summary>
    public double? TotalChangePercent
    {
        get
        {
            var h = PriceHistory.OrderBy(p => p.Date).ToList();
            if (h.Count < 2 || h[0].Cost == 0) return null;
            return (double)((h[^1].Cost - h[0].Cost) / h[0].Cost * 100m);
        }
    }

    public string CycleLabel => Cycle switch
    {
        BillingCycle.CustomDays => $"every {CycleDays}d",
        BillingCycle.SixMonthly => "6-monthly",
        BillingCycle.TwoYearly => "2-yearly",
        BillingCycle.OneOff => "one-off",
        _ => Cycle.ToString().ToLower()
    };
}
