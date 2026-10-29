namespace LifeHub.Domain;

public enum TaskPriority { Low, Normal, High, Urgent }
public enum TaskState { Todo, InProgress, Waiting, Done }
public enum RecurrenceKind { None, Daily, Weekdays, Weekly, Monthly, Yearly, EveryNDays }

public sealed class Recurrence
{
    public RecurrenceKind Kind { get; set; } = RecurrenceKind.None;
    public int Interval { get; set; } = 1;

    public DateOnly Next(DateOnly from) => Kind switch
    {
        RecurrenceKind.Daily => from.AddDays(Math.Max(1, Interval)),
        RecurrenceKind.Weekdays => NextWeekday(from),
        RecurrenceKind.Weekly => from.AddDays(7 * Math.Max(1, Interval)),
        RecurrenceKind.Monthly => from.AddMonths(Math.Max(1, Interval)),
        RecurrenceKind.Yearly => from.AddYears(Math.Max(1, Interval)),
        RecurrenceKind.EveryNDays => from.AddDays(Math.Max(1, Interval)),
        _ => from
    };

    private static DateOnly NextWeekday(DateOnly d)
    {
        do d = d.AddDays(1); while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        return d;
    }

    public override string ToString() => Kind switch
    {
        RecurrenceKind.None => "",
        RecurrenceKind.Weekdays => "Weekdays",
        RecurrenceKind.EveryNDays => $"Every {Interval} days",
        RecurrenceKind.Daily when Interval > 1 => $"Every {Interval} days",
        RecurrenceKind.Weekly when Interval > 1 => $"Every {Interval} weeks",
        RecurrenceKind.Monthly when Interval > 1 => $"Every {Interval} months",
        RecurrenceKind.Yearly when Interval > 1 => $"Every {Interval} years",
        _ => Kind.ToString()
    };
}

public sealed class ChecklistItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

[Owned(OwnScope.Personal)]
public sealed class TaskItem : Entity
{
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Category { get; set; } = "Admin";
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public TaskState State { get; set; } = TaskState.Todo;
    public DateOnly? DueDate { get; set; }
    public TimeOnly? DueTime { get; set; }
    public int? EstimatedMinutes { get; set; }
    /// <summary>Set by the day planner (or by hand) — a time block for doing the task.</summary>
    public DateTime? ScheduledStart { get; set; }
    public Recurrence Recurrence { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<ChecklistItem> Checklist { get; set; } = new();
    /// <summary>manual, email, ai, api, recurring</summary>
    public string Source { get; set; } = "manual";
    public string SourceRef { get; set; } = "";
    public DateTime? CompletedUtc { get; set; }
    public string CalendarEventRef { get; set; } = "";
    /// <summary>How many times this same issue has come in (repeat emails are stacked onto one task).</summary>
    public int Occurrences { get; set; } = 1;
    public DateTime? LastSeenUtc { get; set; }
    /// <summary>Extra emails stacked onto this task (the first one is in SourceRef).</summary>
    public List<Guid> LinkedEmailIds { get; set; } = new();
    public DateOnly? LastRemindedOn { get; set; }

    public bool IsOpen => State != TaskState.Done;

    public DateTime? DueAt => DueDate?.ToDateTime(DueTime ?? new TimeOnly(23, 59));

    public bool IsOverdue(DateTime now) => IsOpen && DueAt is { } due && due < now;
}
