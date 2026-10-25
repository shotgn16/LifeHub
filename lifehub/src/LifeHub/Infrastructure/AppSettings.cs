namespace LifeHub.Infrastructure;

/// <summary>Marks a string setting that is encrypted at rest in settings.json.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute { }

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public AuthSettings Auth { get; set; } = new();
    public HomeAssistantSettings HomeAssistant { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();
    public EmailSettings Email { get; set; } = new();
    public ScheduleSettings Schedule { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public BibleSettings Bible { get; set; } = new();
    public MealSettings Meals { get; set; } = new();
}

public sealed class GeneralSettings
{
    public string HouseholdName { get; set; } = "LifeHub";
    /// <summary>Where you open LifeHub from your phone (e.g. https://lifehub.example.com) — used for links in notifications.</summary>
    public string PublicUrl { get; set; } = "";
    public string TimeZone { get; set; } = "Europe/London";
    public string Currency { get; set; } = "GBP";
    public string Culture { get; set; } = "en-GB";
    public int ExpiryWarningDays { get; set; } = 3;
    public int RenewalWarningDays { get; set; } = 30;
    public int FreezerShelfLifeDays { get; set; } = 90;
    public int DefrostedShelfLifeDays { get; set; } = 1;
    /// <summary>Look up unknown barcodes on Open Food Facts (only the barcode number is sent).</summary>
    public bool BarcodeLookupOnline { get; set; } = true;
    public string BarcodeLookupUrl { get; set; } = "https://world.openfoodfacts.org";
    /// <summary>Scanning a product you've already confirmed adds/uses it straight away (with undo).</summary>
    public bool ScanInstantKnown { get; set; } = true;
    /// <summary>1.3 upgrade flag: shopping items LifeHub added by itself (low stock, predictions, email) were removed once.</summary>
    public bool ShoppingSourcesMigrated { get; set; }
    public List<string> TaskCategories { get; set; } = new() { "Work", "Home DIY", "Smart Home", "Personal Care", "Admin", "Errands" };
    public List<string> FoodCategories { get; set; } = new() { "Dairy", "Meat & Fish", "Fruit & Veg", "Bakery", "Frozen", "Tins & Jars", "Dry Goods", "Drinks", "Snacks", "Household", "Other" };
}

public sealed class AuthSettings
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>When running as an HA add-on, requests arriving through HA ingress are already authenticated by HA.</summary>
    public bool TrustHomeAssistantIngress { get; set; } = true;
    [Secret] public string ApiKey { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(PasswordHash);
}

public sealed class HomeAssistantSettings
{
    public bool Enabled { get; set; }
    /// <summary>e.g. http://homeassistant.local:8123 — ignored when running as an add-on (Supervisor proxy is used).</summary>
    public string Url { get; set; } = "http://homeassistant.local:8123";
    [Secret] public string Token { get; set; } = "";
    public List<string> PresenceEntities { get; set; } = new();
    public List<EnergySensor> EnergySensors { get; set; } = new();
    public List<string> CalendarEntities { get; set; } = new();
    /// <summary>HA calendar used when LifeHub creates events (must support calendar.create_event, e.g. Local Calendar, Google, CalDAV).</summary>
    public string DefaultCalendarEntity { get; set; } = "";
    /// <summary>Legacy single target (kept for upgrades); use NotifyServices.</summary>
    public string NotifyService { get; set; } = "";
    /// <summary>Phones / notify services that get reminders, e.g. notify.mobile_app_pixel_8.</summary>
    public List<string> NotifyServices { get; set; } = new();
    /// <summary>HA to-do list kept in sync with the LifeHub shopping list (e.g. todo.shopping_list).</summary>
    public string ShoppingTodoEntity { get; set; } = "";
    /// <summary>Optional HA to-do list kept in sync with open LifeHub tasks.</summary>
    public string TasksTodoEntity { get; set; } = "";
    /// <summary>Entities hidden from the Home page.</summary>
    public List<string> HiddenEntities { get; set; } = new();
    /// <summary>Set once LifeHub has picked sensible defaults from your HA (people, calendars, phones, energy, lists).</summary>
    public bool AutoSetupDone { get; set; }
    public bool PublishSensors { get; set; } = true;
    public bool NotifyShoppingWhenLeaving { get; set; } = true;
}

public sealed class EnergySensor
{
    public string EntityId { get; set; } = "";
    public string Label { get; set; } = "";
}

public sealed class OllamaSettings
{
    public bool Enabled { get; set; }
    public string Url { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "qwen2.5:7b";
    public int TimeoutSeconds { get; set; } = 180;
    public double Temperature { get; set; } = 0.1;
    /// <summary>Action types (task, event, renewal, shopping…) that are applied without review.</summary>
    public List<string> AutoApplyTypes { get; set; } = new() { "task", "shopping", "promotion" };
    public bool PantryReviewMigrated { get; set; }
    /// <summary>One-off upgrade flag: adds "promotion" to AutoApplyTypes for existing installs.</summary>
    public bool PromotionAutoApplyMigrated { get; set; }
    public double AutoApplyMinConfidence { get; set; } = 0.85;
    /// <summary>Free-text rules appended to the prompt, e.g. "Emails from my boss are always Work".</summary>
    public string ExtraInstructions { get; set; } = "";
    public int MaxEmailChars { get; set; } = 6000;
    /// <summary>Model that can see images (OCR of study notes, groceries from photos), e.g. qwen2.5vl:7b, llama3.2-vision, minicpm-v.</summary>
    public string VisionModel { get; set; } = "";
    /// <summary>Optional embedding model for finding related Bible studies, e.g. nomic-embed-text.</summary>
    public string EmbeddingModel { get; set; } = "";
    /// <summary>Let "thinking" models (qwen3, qwen3-vl, deepseek-r1…) reason before answering. Much slower; off by default.</summary>
    public bool Thinking { get; set; }
    /// <summary>Context window in tokens (Ollama's num_ctx). 8192 fits emails, studies and meal plans on small machines.</summary>
    public int ContextTokens { get; set; } = 8192;
    /// <summary>Upper limit on the length of an answer, so a model can never run on for minutes.</summary>
    public int MaxAnswerTokens { get; set; } = 3072;
}

public sealed class EmailSettings
{
    public int PollMinutes { get; set; } = 10;
    public int MaxPerPoll { get; set; } = 20;
    /// <summary>On first sync only look back this many days.</summary>
    public int InitialLookbackDays { get; set; } = 2;
    public List<EmailAccount> Accounts { get; set; } = new();
    /// <summary>Keep deals from marketing emails in a Promotions list (otherwise they're just ignored).</summary>
    public bool KeepPromotions { get; set; } = true;
    /// <summary>Deleting a task created from an email moves that email to the mailbox's bin.</summary>
    public bool DeleteEmailWithTask { get; set; } = true;
    /// <summary>When a linked email is deleted (in LifeHub or your mail app), its open tasks are deleted too.</summary>
    public bool DeleteTasksWithEmail { get; set; } = true;
}

public enum EmailAccountKind { Imap, MicrosoftGraph, ExchangeEws }

public sealed class EmailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public EmailAccountKind Kind { get; set; } = EmailAccountKind.Imap;
    public bool Enabled { get; set; } = true;
    public string Address { get; set; } = "";

    // IMAP (Gmail with an app password, iCloud, Fastmail, on-prem Exchange with IMAP on…)
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public bool ImapSsl { get; set; } = true;
    public string Username { get; set; } = "";
    [Secret] public string Password { get; set; } = "";
    public string Folder { get; set; } = "INBOX";

    // Microsoft 365 / Outlook.com via Microsoft Graph (device-code sign-in)
    public string GraphClientId { get; set; } = "";
    public string GraphTenant { get; set; } = "common";
    [Secret] public string GraphRefreshToken { get; set; } = "";
    public bool UseForCalendar { get; set; }

    // On-premises Exchange via EWS
    public string EwsUrl { get; set; } = "";
    public string EwsDomain { get; set; } = "";

    // Sync state
    public DateTime? LastSyncUtc { get; set; }
    public uint LastImapUid { get; set; }
    public uint ImapUidValidity { get; set; }
    /// <summary>Graph/EWS: UTC time of the newest message already processed.</summary>
    public DateTime? LastReceivedUtc { get; set; }
    public string LastError { get; set; } = "";
    public List<string> IgnoreSenders { get; set; } = new();
}

public enum PeriodKind { Work, Free, Sleep, Commute, Focus, Other }

public sealed class PeriodBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<DayOfWeek> Days { get; set; } = new();
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
    public PeriodKind Kind { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ScheduleSettings
{
    public List<PeriodBlock> Blocks { get; set; } = DefaultBlocks();
    /// <summary>All-day calendar events containing one of these words turn that day's Work periods into Free time.</summary>
    public List<string> DayOffKeywords { get; set; } = new() { "holiday", "annual leave", "day off", "vacation", "bank holiday", "pto" };
    /// <summary>Task category → period it should be planned into. Unlisted categories go into Free time.</summary>
    public Dictionary<string, PeriodKind> CategoryPeriods { get; set; } = new() { ["Work"] = PeriodKind.Work };
    public int DefaultTaskMinutes { get; set; } = 30;
    public int BufferMinutes { get; set; } = 5;

    /// <summary>Every evening, plan tomorrow's tasks into free slots automatically.</summary>
    public bool AutoPlanEvening { get; set; } = true;
    public TimeOnly AutoPlanEveningTime { get; set; } = new(21, 0);
    /// <summary>Every morning, fit in anything new (e.g. tasks created from overnight emails).</summary>
    public bool AutoPlanMorning { get; set; } = true;
    public TimeOnly AutoPlanMorningTime { get; set; } = new(7, 0);
    public bool AutoPlanAddToCalendar { get; set; }
    /// <summary>Cap on automatically planned tasks per day, so a long backlog doesn't fill every free minute.</summary>
    public int AutoPlanMaxTasks { get; set; } = 6;

    public static List<PeriodBlock> DefaultBlocks()
    {
        var weekdays = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var weekend = new List<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };
        return new()
        {
            new() { Days = weekdays, Start = new(7, 0), End = new(9, 0), Kind = PeriodKind.Free, Label = "Morning" },
            new() { Days = weekdays, Start = new(9, 0), End = new(17, 30), Kind = PeriodKind.Work, Label = "Work" },
            new() { Days = weekdays, Start = new(17, 30), End = new(23, 0), Kind = PeriodKind.Free, Label = "Evening" },
            new() { Days = weekend, Start = new(8, 0), End = new(23, 0), Kind = PeriodKind.Free, Label = "Weekend" },
        };
    }
}

public sealed class NotificationSettings
{
    public bool RenewalReminders { get; set; } = true;
    public bool TaskReminders { get; set; } = true;
    public bool ExpiryReminders { get; set; } = true;
    public bool DailyDigest { get; set; } = true;
    public TimeOnly DailyDigestTime { get; set; } = new(8, 0);
    /// <summary>Where push notifications go: Home Assistant's Companion app, LifeHub's own (web push), or both.</summary>
    public NotifyChannel Channel { get; set; } = NotifyChannel.HomeAssistant;
    /// <summary>VAPID key pair for LifeHub's own web push (generated on first use).</summary>
    public string VapidPublicKey { get; set; } = "";
    [Secret] public string VapidPrivateKey { get; set; } = "";
    /// <summary>Contact sent to push services (mailto: or https:). Defaults to your LifeHub web address.</summary>
    public string VapidSubject { get; set; } = "";
}

public enum NotifyChannel { HomeAssistant, LifeHub, Both }

public sealed class BibleSettings
{
    public bool Enabled { get; set; } = true;

    // ---- Reading tracking ----
    /// <summary>HA sensors reporting the app in use on a phone (Android Companion "Last used app"), e.g. sensor.pixel_8_last_used_app.</summary>
    public List<string> AppSensors { get; set; } = new();
    /// <summary>Text that identifies the Bible app in that sensor (package name or app name).</summary>
    public string AppMatch { get; set; } = "com.sirma.mobile.bible.android";
    /// <summary>A day counts as "read" (and today's verse as done) after this many minutes in the app.</summary>
    public int MinMinutes { get; set; } = 2;
    /// <summary>Time in the Bible app counts as reading the verse of the day.</summary>
    public bool AppCountsAsVerse { get; set; } = true;
    /// <summary>After a reading session in the app, send a "What did you read?" notification with one-tap logging.</summary>
    public bool AskWhatIRead { get; set; }
    /// <summary>Visits to the app shorter than this don't count (opened by accident, a quick glance).</summary>
    public int MinSessionSeconds { get; set; } = 45;

    // ---- Verse of the day (YouVersion Platform API, optional) ----
    /// <summary>Free app key from platform.youversion.com. Without it you can type the day's verse yourself.</summary>
    public string YouVersionAppKey { get; set; } = "";
    /// <summary>Bible version id for verse text and links (e.g. 3034 = BSB, 111 = NIV, 59 = ESV, 1 = KJV).</summary>
    public int VersionId { get; set; } = 3034;
    public string YouVersionApiBase { get; set; } = "https://api.youversion.com";

    // ---- Keeping on track ----
    /// <summary>Unread verses and plan readings move to the next day.</summary>
    public bool CarryOver { get; set; } = true;
    public bool Reminder { get; set; } = true;
    public TimeOnly ReminderTime { get; set; } = new(20, 30);

    // ---- Reading plan ----
    public bool PlanEnabled { get; set; }
    public string PlanBook { get; set; } = "John";
    public int PlanNextChapter { get; set; } = 1;
    public int PlanChaptersPerDay { get; set; } = 1;

    // ---- Study helpers (only when you press "Ask for insights") ----
    public bool StudyLightEnabled { get; set; } = true;
    /// <summary>{book} {chapter} {verse} {usfm} placeholders.</summary>
    public string StudyLightUrl { get; set; } = "https://www.studylight.org/commentary/{book}/{chapter}-{verse}.html";
    public bool BibliaGraphiaEnabled { get; set; } = true;

    // ---- Go deeper ----
    /// <summary>1 = Overview (quick), 2 = Detailed, 3 = In-depth study. Deeper reads more StudyLight commentary and takes longer.</summary>
    public int DeeperLevel { get; set; } = 2;
    /// <summary>Include original-language (Hebrew/Greek) word studies.</summary>
    public bool DeeperWordStudies { get; set; } = true;
    /// <summary>Include reflection questions and a prayer.</summary>
    public bool DeeperReflection { get; set; } = true;
    /// <summary>Where you'd like application to focus, e.g. "family life, my small group, work".</summary>
    public string DeeperFocus { get; set; } = "";
    public string BibliaGraphiaUrl { get; set; } = "https://bibliagraphia.com/";
}

public sealed class MealSettings
{
    /// <summary>Dietary needs applied to every suggestion, e.g. "gluten free", "no pork", "nut allergy".</summary>
    public List<string> Requirements { get; set; } = new();
    /// <summary>Free-text preferences, e.g. "we like spicy food, quick weekday dinners".</summary>
    public string Preferences { get; set; } = "";
    public int DefaultServings { get; set; } = 2;
    /// <summary>Meals planned for these slots.</summary>
    public List<string> Slots { get; set; } = new() { "Dinner" };
}
