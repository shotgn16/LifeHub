namespace LifeHub.Domain;

public enum SuggestionStatus { Pending, Applied, Rejected, Failed, Undone }

/// <summary>An action the AI proposed (from an email or from quick capture), waiting for review.</summary>
[Owned(OwnScope.Personal)]
public sealed class AiSuggestion : Entity
{
    /// <summary>Handler type, e.g. task, event, renewal, shopping. See Ai/Handlers.</summary>
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly? Date { get; set; }
    public TimeOnly? Time { get; set; }
    public int? DurationMinutes { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public double Confidence { get; set; }
    /// <summary>Any extra fields a custom handler wants (free-form key/values from the model).</summary>
    public Dictionary<string, string> Extra { get; set; } = new();

    public SuggestionStatus Status { get; set; } = SuggestionStatus.Pending;
    public string Result { get; set; } = "";
    public bool AutoApplied { get; set; }

    public Guid? EmailId { get; set; }
    /// <summary>email, capture, api</summary>
    public string Origin { get; set; } = "email";
    public string OriginText { get; set; } = "";
}

/// <summary>A processed email (body is not kept, only what's needed to show context).</summary>
[Owned(OwnScope.Personal)]
public sealed class EmailRecord : Entity
{
    public Guid AccountId { get; set; }
    public string AccountName { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime ReceivedUtc { get; set; }
    public string Snippet { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Importance { get; set; } = "normal";
    /// <summary>What kind of email the AI thinks this is (see EmailCategories).</summary>
    public string Category { get; set; } = "";
    public int SuggestionCount { get; set; }
    public string Error { get; set; } = "";
    public string ProviderRef { get; set; } = "";
    public string WebLink { get; set; } = "";
    /// <summary>Set when the message was moved to the bin (by LifeHub or by you in your mail app).</summary>
    public DateTime? DeletedUtc { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
}

public static class EmailCategories
{
    public const string ActionNeeded = "action_needed";
    public const string Personal = "personal";
    public const string Work = "work";
    public const string Bill = "bill";
    public const string Receipt = "receipt";
    public const string Delivery = "delivery";
    public const string Booking = "booking";
    public const string Account = "account";
    public const string Promotion = "promotion";
    public const string Newsletter = "newsletter";
    public const string Notification = "notification";
    public const string Other = "other";

    public static readonly string[] All =
        { ActionNeeded, Personal, Work, Bill, Receipt, Delivery, Booking, Account, Promotion, Newsletter, Notification, Other };

    /// <summary>Marketing: never turned into tasks, shopping, events or renewals.</summary>
    public static bool IsMarketing(string? c) => c is Promotion or Newsletter;

    public static string Label(string? c) => c switch
    {
        ActionNeeded => "Action needed", Bill => "Bill", Receipt => "Receipt", Delivery => "Delivery", Booking => "Booking",
        Account => "Account", Promotion => "Promotion", Newsletter => "Newsletter", Notification => "Notification",
        Personal => "Personal", Work => "Work", _ => "Other"
    };
}

/// <summary>A deal / advert the AI found in a marketing email — kept separate from your shopping list.</summary>
[Owned(OwnScope.Personal)]
public sealed class Promotion : Entity
{
    public string Title { get; set; } = "";
    public string Merchant { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "";
    /// <summary>When the offer ends, if the email says.</summary>
    public DateOnly? EndsOn { get; set; }
    public Guid? EmailId { get; set; }
    public string WebLink { get; set; } = "";
    public bool Dismissed { get; set; }
    public bool Saved { get; set; }
}

public enum NoticeLevel { Info, Warning, Alert }

/// <summary>In-app notification (also pushed to HA if a notify service is set).</summary>
[Owned(OwnScope.Personal)]
public sealed class Notice : Entity
{
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public NoticeLevel Level { get; set; }
    public string Link { get; set; } = "";
    public bool Read { get; set; }
    /// <summary>Prevents the same reminder being sent twice.</summary>
    public string DedupeKey { get; set; } = "";
    /// <summary>What kind of notice it is (see <see cref="NoticeCategories"/>); decides where it goes.</summary>
    public string Category { get; set; } = NoticeCategories.System;
    /// <summary>Whether phones should show it as a notification (otherwise it's only listed in LifeHub).</summary>
    public bool Phone { get; set; }
}

/// <summary>The kinds of notification, each with its own setting in Settings → Notifications.</summary>
public static class NoticeCategories
{
    public const string Reminders = "reminders";
    public const string Renewals = "renewals";
    public const string Food = "food";
    public const string Email = "email";
    public const string Digest = "digest";
    public const string Deals = "deals";
    public const string Shopping = "shopping";
    public const string System = "system";
    public const string HeadsUp = "headsup";

    public static readonly (string Key, string Title, string Help, NoticeRoute Default)[] All =
    {
        (Reminders, "Task reminders", "When a task is due or planned to start", NoticeRoute.Phone),
        (Renewals, "Renewals", "Before something renews or expires", NoticeRoute.Phone),
        (Food, "Food to use up", "Food in the kitchen nearing its date", NoticeRoute.Phone),
        (Email, "From your email", "What the AI added from your email (offers are never notified)", NoticeRoute.Phone),
        (Digest, "Morning summary", "Your day at a glance, at the time you choose", NoticeRoute.Phone),
        (HeadsUp, "Heads-ups", "Coming up in your calendar, with what LifeHub remembers about it", NoticeRoute.Phone),
        (Deals, "Deals", "Offers that suit your meal plan and list", NoticeRoute.InApp),
        (Shopping, "Shopping list", "When LifeHub tidies the shopping list", NoticeRoute.InApp),
        (System, "LifeHub itself", "Backups, updates and problems that need you", NoticeRoute.InApp),
    };

    public static string Icon(string category) => category switch
    {
        Reminders => "⏰", Renewals => "🔁", Food => "🥛", Email => "📬", Digest => "☀️", HeadsUp => "💡", Deals => "🏷", Shopping => "🛒", _ => "ℹ️"
    };
}

/// <summary>Where a kind of notification goes.</summary>
public enum NoticeRoute { Phone, InApp, Off }
