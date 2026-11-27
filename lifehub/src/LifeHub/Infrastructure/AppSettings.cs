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
    public MealSettings Meals { get; set; } = new();
    public DealSettings Deals { get; set; } = new();
    /// <summary>The people using LifeHub. The first admin is the "main person" who keeps household data when sharing is off.</summary>
    public List<UserProfile> Users { get; set; } = new();
    public HouseholdSettings Household { get; set; } = new();
    public BackgroundSettings Background { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
}

/// <summary>The household's Telegram bot (Settings → Telegram). Each person links their own chat to it.</summary>
public sealed class TelegramSettings
{
    public bool Enabled { get; set; }
    /// <summary>From @BotFather. Anyone with it can read and send the bot's messages, so it's stored encrypted.</summary>
    [Secret] public string BotToken { get; set; } = "";
    /// <summary>The bot's @username and name, filled in when it's connected.</summary>
    public string BotUsername { get; set; } = "";
    public string BotName { get; set; } = "";
    public bool IsReady => Enabled && !string.IsNullOrWhiteSpace(BotToken);
}

/// <summary>Names of LifeHub's background jobs (Settings → Background tasks).</summary>
public static class BackgroundJobs
{
    public const string Email = "email";
    public const string Reminders = "reminders";
    public const string AutoPlan = "autoplan";
    public const string HaSensors = "ha-sensors";
    public const string HaLists = "ha-lists";
    public const string Presence = "presence";
    public const string Deals = "deals";
    public const string Backup = "backup";
    public const string Tidy = "tidy";
    public const string Telegram = "telegram";
}

/// <summary>Which background jobs run, and how often (Settings → Background tasks).</summary>
public sealed class BackgroundSettings
{
    /// <summary>Jobs switched off.</summary>
    public HashSet<string> Disabled { get; set; } = new();
    /// <summary>Publish sensor.lifehub_* to Home Assistant every N minutes.</summary>
    public int SensorMinutes { get; set; } = 5;
    /// <summary>Sync the Home Assistant shopping/tasks lists at least every N seconds (and straight after changes).</summary>
    public int HaListSeconds { get; set; } = 60;
    /// <summary>The nightly backup runs from this hour (local time).</summary>
    public int BackupHour { get; set; } = 3;
    public int KeepBackups { get; set; } = 14;

    public bool IsOn(string job) => !Disabled.Contains(job);
}

/// <summary>What the household shares. Tasks, email and notes are always each person's own.</summary>
public sealed class HouseholdSettings
{
    /// <summary>Food stock, the shopping list and purchase history.</summary>
    public bool ShareKitchen { get; set; } = true;
    /// <summary>Recipes and the meal plan.</summary>
    public bool ShareMeals { get; set; } = true;
    /// <summary>Renewals and subscriptions.</summary>
    public bool ShareRenewals { get; set; } = true;
}

/// <summary>One person using LifeHub, with their own sign-in, phones, calendars and schedule.</summary>
public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// <summary>For signing in on the direct port / your own address. Optional when everyone uses the Home Assistant app.</summary>
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    /// <summary>Optional PIN asked for on the "Who's using LifeHub?" screen in Home Assistant.</summary>
    public string PinHash { get; set; } = "";
    /// <summary>Admins change household settings (Home Assistant, AI, people, sharing).</summary>
    public bool IsAdmin { get; set; }
    public string Colour { get; set; } = "#0f766e";
    [Secret] public string ApiKey { get; set; } = "";
    /// <summary>Their Home Assistant person (person.alex) — for "while you're out" shopping reminders.</summary>
    public string PersonEntity { get; set; } = "";

    // ---- Personal settings (shown in the matching Settings tabs when this person is signed in) ----
    public List<string> NotifyServices { get; set; } = new();
    public List<string> CalendarEntities { get; set; } = new();
    public string DefaultCalendarEntity { get; set; } = "";
    public string TasksTodoEntity { get; set; } = "";
    public NotifyChannel Channel { get; set; } = NotifyChannel.HomeAssistant;
    public bool TaskReminders { get; set; } = true;
    public bool RenewalReminders { get; set; } = true;
    public bool ExpiryReminders { get; set; } = true;
    public bool DailyDigest { get; set; } = true;
    public TimeOnly DailyDigestTime { get; set; } = new(8, 0);
    public Dictionary<string, NoticeRoute> NoticeRoutes { get; set; } = new();
    public bool QuietHours { get; set; }
    public TimeOnly QuietFrom { get; set; } = new(22, 0);
    public TimeOnly QuietTo { get; set; } = new(7, 0);
    public ScheduleSettings Schedule { get; set; } = new();
    /// <summary>Their Telegram chat with the household's bot (Settings → Telegram), and whether notifications go there too.</summary>
    public long? TelegramChatId { get; set; }
    public string TelegramName { get; set; } = "";
    public bool TelegramNotify { get; set; } = true;

    // ---- Appearance (Settings → Appearance) ----
    /// <summary>auto (follow the device) · light · dark</summary>
    public string Theme { get; set; } = "auto";
    /// <summary>Accent colour, e.g. #0f766e (empty = the theme's).</summary>
    public string Accent { get; set; } = "";
    /// <summary>comfortable · compact</summary>
    public string Density { get; set; } = "comfortable";
    /// <summary>Home screen cards this person has hidden, and the order they chose (module keys).</summary>
    public List<string> DashboardHidden { get; set; } = new();
    public List<string> DashboardOrder { get; set; } = new();

    public bool HasPassword => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(PasswordHash);
    public string Initials => string.Concat((string.IsNullOrWhiteSpace(Name) ? Username : Name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(w => char.ToUpperInvariant(w[0])));
    public string FirstName => (string.IsNullOrWhiteSpace(Name) ? Username : Name).Trim().Split(' ', 2)[0];
    /// <summary>"Alex Smith" → "alex_smith", for per-person HA sensors.</summary>
    public string Slug => System.Text.RegularExpressions.Regex.Replace((string.IsNullOrWhiteSpace(Name) ? Username : Name).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
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
    /// <summary>Modules switched off for the whole household (Settings → Modules), e.g. "renewals".</summary>
    public List<string> DisabledModules { get; set; } = new();
    /// <summary>Extra CSS applied after the theme (Settings → Appearance), for restyling LifeHub without changing code.</summary>
    public string CustomCss { get; set; } = "";
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

/// <summary>Who runs the AI.</summary>
public enum AiProvider
{
    /// <summary>Ollama on this computer or your network (private, free).</summary>
    Ollama,
    /// <summary>OpenAI's ChatGPT models (needs an API key; your text goes to OpenAI).</summary>
    OpenAI,
    /// <summary>Google Gemini (needs an API key; your text goes to Google).</summary>
    Gemini,
    /// <summary>Anthropic's Claude (needs an API key; your text goes to Anthropic).</summary>
    Claude,
    /// <summary>Any server that speaks the OpenAI API: LM Studio, llama.cpp, vLLM, OpenRouter, Groq…</summary>
    OpenAICompatible,
    /// <summary>A model running inside the phone app itself (no server, works offline).</summary>
    OnDevice
}

/// <summary>AI settings (kept under "ollama" in settings.json for compatibility with earlier versions).</summary>
public sealed class OllamaSettings
{
    public bool Enabled { get; set; }
    public AiProvider Provider { get; set; } = AiProvider.Ollama;
    /// <summary>API key for ChatGPT, Gemini, Claude or an OpenAI-compatible service.</summary>
    [Secret] public string ApiKey { get; set; } = "";
    /// <summary>Address of an OpenAI-compatible server, e.g. http://localhost:1234/v1.</summary>
    public string BaseUrl { get; set; } = "";
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
    /// <summary>Optional embedding model (not used by LifeHub 3; kept so older settings files load).</summary>
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
    /// <summary>The Microsoft app (client) ID for "Sign in with Microsoft" (empty = the one built into LifeHub, if any).</summary>
    public string MicrosoftClientId { get; set; } = "";
    /// <summary>Check every mailbox when someone opens LifeHub (or the phone app), at most every 2 minutes.</summary>
    public bool SyncOnOpen { get; set; } = true;
}

public enum EmailAccountKind { Imap, MicrosoftGraph, ExchangeEws }

public sealed class EmailAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>The person whose mailbox this is (empty = the main person, for accounts added before 2.0).</summary>
    public Guid OwnerId { get; set; }
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

public enum PeriodKind { Work, Free, Sleep, Commute, Focus, Other, Out, Reserved }

/// <summary>One day of your usual week: whether you work, and when.</summary>
public sealed class UsualDay
{
    public DayOfWeek Day { get; set; }
    public bool Working { get; set; }
    public TimeOnly WorkStart { get; set; } = new(9, 0);
    public TimeOnly WorkEnd { get; set; } = new(17, 30);
}

/// <summary>Something you do every week at the same time (gym, school run, a club) — nothing gets planned then.</summary>
public sealed class RegularTime
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public List<DayOfWeek> Days { get; set; } = new();
    public TimeOnly Start { get; set; } = new(18, 0);
    public TimeOnly End { get; set; } = new(19, 0);
}

/// <summary>
/// Your usual week in plain terms (Schedule → My usual week). LifeHub turns it into the detailed periods
/// (<see cref="ScheduleSettings.Blocks"/>) that the day view and "Plan my day" use.
/// </summary>
public sealed class UsualWeek
{
    public static readonly DayOfWeek[] Order = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

    public List<UsualDay> Days { get; set; } = Order.Select(d => new UsualDay { Day = d, Working = d is not (DayOfWeek.Saturday or DayOfWeek.Sunday) }).ToList();
    /// <summary>When your day starts and ends on days you work…</summary>
    public TimeOnly WorkDayFrom { get; set; } = new(7, 0);
    public TimeOnly WorkDayTo { get; set; } = new(23, 0);
    /// <summary>…and on days you don't.</summary>
    public TimeOnly OffDayFrom { get; set; } = new(8, 0);
    public TimeOnly OffDayTo { get; set; } = new(23, 0);
    public List<RegularTime> Regular { get; set; } = new();

    public UsualDay Of(DayOfWeek d) => Days.FirstOrDefault(x => x.Day == d) ?? new UsualDay { Day = d };

    /// <summary>The detailed periods for this week: work, regular times, and free time around them.</summary>
    public List<PeriodBlock> ToBlocks()
    {
        var perDay = new List<(DayOfWeek Day, TimeOnly Start, TimeOnly End, PeriodKind Kind, string Label)>();
        foreach (var d in Order)
        {
            var u = Of(d);
            var from = u.Working ? WorkDayFrom : OffDayFrom;
            var to = u.Working ? WorkDayTo : OffDayTo;
            var busy = new List<(TimeOnly Start, TimeOnly End, PeriodKind Kind, string Label)>();
            if (u.Working) busy.Add((u.WorkStart, u.WorkEnd, PeriodKind.Work, "Work"));
            foreach (var r in Regular.Where(r => r.Days.Contains(d) && r.End > r.Start))
                busy.Add((r.Start, r.End, PeriodKind.Reserved, string.IsNullOrWhiteSpace(r.Label) ? "Reserved" : r.Label.Trim()));
            foreach (var b in busy) perDay.Add((d, b.Start, b.End, b.Kind, b.Label));

            // Free time is the rest of the waking day (an overnight shift only blocks until midnight here).
            var free = new List<(TimeOnly, TimeOnly)>();
            if (to > from) free.Add((from, to));
            foreach (var b in busy)
            {
                var end = b.End <= b.Start ? TimeOnly.MaxValue : b.End;
                free = free.SelectMany(f => Cut(f, (b.Start, end))).ToList();
            }
            var workStart = u.Working ? u.WorkStart : (TimeOnly?)null;
            foreach (var (s, e) in free.Where(f => (f.Item2 - f.Item1).TotalMinutes >= 15))
            {
                var label = !u.Working ? (d is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Weekend" : "Day off")
                          : s < workStart ? "Morning" : "Evening";
                perDay.Add((d, s, e, PeriodKind.Free, label));
            }
        }
        // Days with exactly the same period share one block, so the detailed list stays short.
        return perDay.GroupBy(p => (p.Start, p.End, p.Kind, p.Label))
            .Select(g => new PeriodBlock { Days = g.Select(x => x.Day).ToList(), Start = g.Key.Start, End = g.Key.End, Kind = g.Key.Kind, Label = g.Key.Label })
            .OrderBy(b => b.Days.Select(x => Array.IndexOf(Order, x)).Min()).ThenBy(b => b.Start).ToList();
    }

    private static IEnumerable<(TimeOnly, TimeOnly)> Cut((TimeOnly Start, TimeOnly End) span, (TimeOnly Start, TimeOnly End) busy)
    {
        if (busy.End <= span.Start || busy.Start >= span.End) { yield return span; yield break; }
        if (busy.Start > span.Start) yield return (span.Start, busy.Start);
        if (busy.End < span.End) yield return (busy.End, span.End);
    }

    /// <summary>A best guess at the usual week from detailed periods (older versions, or the detailed editor).</summary>
    public static UsualWeek FromBlocks(IEnumerable<PeriodBlock> blocks, out bool exact)
    {
        var list = blocks.ToList();
        var week = new UsualWeek { Days = new() };
        var workFrom = new List<TimeOnly>(); var workTo = new List<TimeOnly>();
        var offFrom = new List<TimeOnly>(); var offTo = new List<TimeOnly>();
        foreach (var d in Order)
        {
            var today = list.Where(b => b.Days.Contains(d)).ToList();
            var work = today.Where(b => b.Kind == PeriodKind.Work).ToList();
            var day = new UsualDay { Day = d, Working = work.Count > 0 };
            if (work.Count > 0) { day.WorkStart = work.Min(b => b.Start); day.WorkEnd = work.Max(b => b.End); }
            week.Days.Add(day);
            if (today.Count == 0) continue;
            (day.Working ? workFrom : offFrom).Add(today.Min(b => b.Start));
            (day.Working ? workTo : offTo).Add(today.Max(b => b.End <= b.Start ? new TimeOnly(23, 59) : b.End));
        }
        if (workFrom.Count > 0) { week.WorkDayFrom = workFrom.Min(); week.WorkDayTo = workTo.Max(); }
        if (offFrom.Count > 0) { week.OffDayFrom = offFrom.Min(); week.OffDayTo = offTo.Max(); }
        foreach (var r in list.Where(b => b.Kind == PeriodKind.Reserved))
            week.Regular.Add(new RegularTime { Label = r.Label, Days = new(r.Days), Start = r.Start, End = r.End });
        // Exact when rebuilding gives the same periods back (true for the default week).
        exact = Same(week.ToBlocks(), list);
        return week;
    }

    private static bool Same(List<PeriodBlock> a, List<PeriodBlock> b)
    {
        static IEnumerable<string> Keys(IEnumerable<PeriodBlock> x) => x.SelectMany(p => p.Days.Select(d => $"{d}|{p.Start}|{p.End}|{p.Kind}")).OrderBy(k => k);
        return Keys(a).SequenceEqual(Keys(b));
    }
}

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
    /// <summary>Your usual week as set in Schedule → My usual week; null when the periods were set up in detail instead.</summary>
    public UsualWeek? Usual { get; set; }
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
    /// <summary>Per kind of notification: on the phone (and in LifeHub), only in LifeHub, or off. Missing = the default.</summary>
    public Dictionary<string, NoticeRoute> Routes { get; set; } = new();
    /// <summary>No phone notifications between these times (they still appear in LifeHub). Equal times = off.</summary>
    public TimeOnly QuietFrom { get; set; } = new(22, 0);
    public TimeOnly QuietTo { get; set; } = new(7, 0);
    public bool QuietHours { get; set; }

    public NoticeRoute RouteOf(string category) =>
        Routes.TryGetValue(category, out var r) ? r : NoticeCategories.All.FirstOrDefault(c => c.Key == category).Default;
    /// <summary>Where push notifications go: Home Assistant's Companion app, LifeHub's own (web push), or both.</summary>
    public NotifyChannel Channel { get; set; } = NotifyChannel.HomeAssistant;
    /// <summary>VAPID key pair for LifeHub's own web push (generated on first use).</summary>
    public string VapidPublicKey { get; set; } = "";
    [Secret] public string VapidPrivateKey { get; set; } = "";
    /// <summary>Contact sent to push services (mailto: or https:). Defaults to your LifeHub web address.</summary>
    public string VapidSubject { get; set; } = "";
}

public enum NotifyChannel { HomeAssistant, LifeHub, Both }

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

/// <summary>A store whose offers LifeHub checks.</summary>
public sealed class DealStore
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    /// <summary>The store's offers page. Some stores build these pages in the browser; their emails and leaflets still work.</summary>
    public string OffersUrl { get; set; } = "";
    /// <summary>Names to recognise the store by (nearby branches on OpenStreetMap, email senders).</summary>
    public List<string> Match { get; set; } = new();
}

public sealed class NearbyStore
{
    public string Store { get; set; } = "";
    public string Name { get; set; } = "";
    public double Miles { get; set; }
}

/// <summary>Deals & offers: periodically checks stores near you and suggests meals and ingredients that fit your week.</summary>
public sealed class DealSettings
{
    public bool Enabled { get; set; }
    /// <summary>A town, city or postcode, e.g. "Leeds" or "LS1 4AP".</summary>
    public string Area { get; set; } = "";
    public int RadiusMiles { get; set; } = 5;
    /// <summary>Only check stores with a branch within the radius.</summary>
    public bool NearbyOnly { get; set; } = true;
    /// <summary>Also use supermarket offers from your newsletters (the AI already files these as promotions).</summary>
    public bool UseEmails { get; set; } = true;
    public int CheckEveryHours { get; set; } = 24;
    /// <summary>Let the AI suggest meals and ingredients from the deals, on days that suit them.</summary>
    public bool SuggestMeals { get; set; } = true;
    public bool Notify { get; set; } = true;
    public List<DealStore> Stores { get; set; } = DefaultStores();

    // Lookups (OpenStreetMap / postcodes.io — only the area you type is sent)
    public string PostcodeApi { get; set; } = "https://api.postcodes.io";
    public string GeocodeApi { get; set; } = "https://nominatim.openstreetmap.org";
    public string OverpassApi { get; set; } = "https://overpass-api.de/api/interpreter";

    // Results of the last check
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string AreaLabel { get; set; } = "";
    public string LocatedArea { get; set; } = "";
    public List<NearbyStore> Nearby { get; set; } = new();
    public DateTime? LastCheckUtc { get; set; }
    public string LastResult { get; set; } = "";
    public Dictionary<string, string> StoreStatus { get; set; } = new();

    public static List<DealStore> DefaultStores() => new()
    {
        new() { Name = "Tesco", OffersUrl = "https://www.tesco.com/groceries/en-GB/promotions", Match = new() { "tesco" } },
        new() { Name = "Sainsbury's", OffersUrl = "https://www.sainsburys.co.uk/gol-ui/features/offers", Match = new() { "sainsbury" } },
        new() { Name = "Asda", OffersUrl = "https://www.asda.com/groceries/rollback", Match = new() { "asda" } },
        new() { Name = "Morrisons", OffersUrl = "https://groceries.morrisons.com/offers", Match = new() { "morrisons" } },
        new() { Name = "Aldi", OffersUrl = "https://www.aldi.co.uk/c/groceries/super-6", Match = new() { "aldi" } },
        new() { Name = "Lidl", OffersUrl = "https://www.lidl.co.uk/c/our-offers/s10024131", Match = new() { "lidl" } },
        new() { Name = "Co-op", OffersUrl = "https://www.coop.co.uk/offers", Match = new() { "co-op", "co-operative", "coop" } },
        new() { Name = "M&S", OffersUrl = "https://www.marksandspencer.com/c/food", Match = new() { "m&s", "marks & spencer", "marks and spencer" } },
        new() { Name = "Waitrose", OffersUrl = "https://www.waitrose.com/ecom/shop/browse/offers", Match = new() { "waitrose" } },
        new() { Name = "Iceland", OffersUrl = "https://www.iceland.co.uk/offers", Match = new() { "iceland" } },
    };
}
