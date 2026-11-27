namespace LifeHub.Domain;

/// <summary>What a one-off entry on the schedule means.</summary>
public enum PlanKind
{
    /// <summary>Holiday, day off, sick day: work time becomes free time.</summary>
    DayOff,
    /// <summary>Out of the house (appointment, visiting someone): nothing is planned then.</summary>
    Out,
    /// <summary>Time kept for something, even if it isn't in a calendar: nothing is planned then.</summary>
    Reserved,
    /// <summary>Working when you normally wouldn't (an extra shift, different hours).</summary>
    Working
}

/// <summary>
/// A one-off change to your usual week, made in Schedule (or by asking LifeHub): "holiday 3–7 March",
/// "out Thursday 2–4pm", "working Saturday 9–1". It doesn't need to be in any calendar.
/// </summary>
[Owned(OwnScope.Personal)]
public sealed class SchedulePlan : Entity
{
    public PlanKind Kind { get; set; }
    /// <summary>What it's for, e.g. "Holiday", "Dentist", "Gym". Empty = the kind's own name.</summary>
    public string Label { get; set; } = "";
    public DateOnly From { get; set; }
    /// <summary>Last day (the same as <see cref="From"/> for a single day).</summary>
    public DateOnly To { get; set; }
    /// <summary>Start and end time; both empty = all day.</summary>
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    /// <summary>Where it came from: "you", "ask", "telegram".</summary>
    public string Source { get; set; } = "you";

    public bool AllDay => Start is null || End is null;
    public bool Covers(DateOnly date) => date >= From && date <= (To < From ? From : To);

    public string Name => !string.IsNullOrWhiteSpace(Label) ? Label : KindName(Kind);

    public static string KindName(PlanKind k) => k switch
    {
        PlanKind.DayOff => "Day off",
        PlanKind.Out => "Out",
        PlanKind.Reserved => "Reserved",
        _ => "Working"
    };

    /// <summary>"Mon 3 Mar", "Mon 3 – Fri 7 Mar", plus the times when it isn't all day.</summary>
    public string When(System.Globalization.CultureInfo culture)
    {
        var to = To < From ? From : To;
        var days = to == From ? From.ToString("ddd d MMM", culture) : $"{From.ToString("ddd d MMM", culture)} – {to.ToString("ddd d MMM", culture)}";
        return AllDay ? days : $"{days}, {Start:HH:mm}–{End:HH:mm}";
    }
}
