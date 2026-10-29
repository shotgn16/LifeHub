using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace LifeHub.Infrastructure;

/// <summary>
/// Loads/saves settings.json. Properties marked [Secret] are encrypted with ASP.NET Data
/// Protection (keys live in data/keys — back that folder up together with settings.json).
/// </summary>
public sealed class SettingsService
{
    private const string Prefix = "enc:";
    private readonly DataPaths _paths;
    private readonly IDataProtector _protector;
    private readonly ILogger<SettingsService> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings _current;

    public event Action? Changed;

    public SettingsService(DataPaths paths, IDataProtectionProvider dp, ILogger<SettingsService> log)
    {
        _paths = paths;
        _log = log;
        _protector = dp.CreateProtector("LifeHub.Settings.v1");
        _current = Load();
        if (EnsureUsers(_current)) Save(_current);
        Publish(_current);
    }

    /// <summary>
    /// A copy of the current settings (decrypted), as seen by the current person: their own phones, calendars,
    /// schedule, Bible settings and mailboxes take the place of the household ones. Edit it, then pass to UpdateAsync.
    /// </summary>
    public AppSettings Current
    {
        get
        {
            var view = Json.Clone(_current);
            if (UserScope.UserId is { } id) Overlay(view, id);
            return view;
        }
    }

    /// <summary>The household settings without anyone's personal overlay (household-level jobs and the People tab).</summary>
    public AppSettings Household => Json.Clone(_current);

    public IReadOnlyList<UserProfile> Users => Json.Clone(_current.Users);
    public UserProfile? User(Guid id) => Json.Clone(_current.Users.FirstOrDefault(u => u.Id == id));
    /// <summary>The signed-in person (null in background work).</summary>
    public UserProfile? Me => UserScope.UserId is { } id ? User(id) : null;
    public Guid PrimaryUserId => Primary(_current)?.Id ?? Guid.Empty;
    /// <summary>At least one person can sign in with a username and password (direct port / own hostname).</summary>
    public bool HasLogin => _current.Users.Any(u => u.HasPassword);

    private static UserProfile? Primary(AppSettings s) => s.Users.FirstOrDefault(u => u.IsAdmin) ?? s.Users.FirstOrDefault();

    private static bool OwnedBy(EmailAccount a, Guid user, Guid primary) => a.OwnerId == user || (a.OwnerId == Guid.Empty && user == primary);

    /// <summary>Puts a person's own settings into the view they get from <see cref="Current"/>.</summary>
    private static void Overlay(AppSettings view, Guid userId)
    {
        var u = view.Users.FirstOrDefault(x => x.Id == userId);
        if (u is null) return;
        var primary = Primary(view)?.Id ?? Guid.Empty;
        var ha = view.HomeAssistant;
        ha.NotifyServices = new(u.NotifyServices);
        ha.NotifyService = "";
        ha.CalendarEntities = new(u.CalendarEntities);
        ha.DefaultCalendarEntity = u.DefaultCalendarEntity;
        ha.TasksTodoEntity = u.TasksTodoEntity;
        var n = view.Notifications;
        n.Channel = u.Channel;
        n.TaskReminders = u.TaskReminders;
        n.RenewalReminders = u.RenewalReminders;
        n.ExpiryReminders = u.ExpiryReminders;
        n.DailyDigest = u.DailyDigest;
        n.DailyDigestTime = u.DailyDigestTime;
        view.Schedule = Json.Clone(u.Schedule);
        var bible = Json.Clone(u.Bible);
        BibleSettings.CopyHousehold(view.Bible, bible);
        view.Bible = bible;
        view.Email.Accounts = view.Email.Accounts.Where(a => OwnedBy(a, userId, primary)).ToList();
    }

    /// <summary>The reverse of <see cref="Overlay"/>: personal parts of an edited view go back into the person's profile.</summary>
    private static AppSettings Split(AppSettings working, AppSettings before, Guid userId)
    {
        var result = Json.Clone(working);
        var u = result.Users.FirstOrDefault(x => x.Id == userId);
        if (u is null) return result;
        var primary = Primary(before)?.Id ?? Guid.Empty;
        u.NotifyServices = new(working.HomeAssistant.NotifyServices);
        u.CalendarEntities = new(working.HomeAssistant.CalendarEntities);
        u.DefaultCalendarEntity = working.HomeAssistant.DefaultCalendarEntity;
        u.TasksTodoEntity = working.HomeAssistant.TasksTodoEntity;
        u.Channel = working.Notifications.Channel;
        u.TaskReminders = working.Notifications.TaskReminders;
        u.RenewalReminders = working.Notifications.RenewalReminders;
        u.ExpiryReminders = working.Notifications.ExpiryReminders;
        u.DailyDigest = working.Notifications.DailyDigest;
        u.DailyDigestTime = working.Notifications.DailyDigestTime;
        u.Schedule = Json.Clone(working.Schedule);
        u.Bible = Json.Clone(working.Bible);

        result.HomeAssistant.NotifyServices = new(before.HomeAssistant.NotifyServices);
        result.HomeAssistant.NotifyService = before.HomeAssistant.NotifyService;
        result.HomeAssistant.CalendarEntities = new(before.HomeAssistant.CalendarEntities);
        result.HomeAssistant.DefaultCalendarEntity = before.HomeAssistant.DefaultCalendarEntity;
        result.HomeAssistant.TasksTodoEntity = before.HomeAssistant.TasksTodoEntity;
        result.Notifications.Channel = before.Notifications.Channel;
        result.Notifications.TaskReminders = before.Notifications.TaskReminders;
        result.Notifications.RenewalReminders = before.Notifications.RenewalReminders;
        result.Notifications.ExpiryReminders = before.Notifications.ExpiryReminders;
        result.Notifications.DailyDigest = before.Notifications.DailyDigest;
        result.Notifications.DailyDigestTime = before.Notifications.DailyDigestTime;
        result.Schedule = Json.Clone(before.Schedule);
        var bible = Json.Clone(before.Bible);
        BibleSettings.CopyHousehold(working.Bible, bible);
        result.Bible = bible;

        foreach (var a in working.Email.Accounts) if (a.OwnerId == Guid.Empty) a.OwnerId = userId;
        result.Email.Accounts = before.Email.Accounts.Where(a => !OwnedBy(a, userId, primary))
            .Concat(Json.Clone(working.Email.Accounts)).ToList();
        return result;
    }

    /// <summary>
    /// 2.0 upgrade (and first start): there is always at least one person. The existing login, API key, phones,
    /// calendars, schedule and Bible settings become the first person's own; the household copies of the schedule
    /// and Bible settings stay as the starting point for people added later.
    /// </summary>
    private static bool EnsureUsers(AppSettings s)
    {
        if (s.Users.Count > 0)
        {
            if (!s.Users.Any(u => u.IsAdmin)) { s.Users[0].IsAdmin = true; return true; }
            return false;
        }
        var name = string.IsNullOrWhiteSpace(s.Auth.Username) ? "Me" : char.ToUpperInvariant(s.Auth.Username.Trim()[0]) + s.Auth.Username.Trim()[1..];
        var ha = s.HomeAssistant;
        var u = new UserProfile
        {
            Name = name, Username = s.Auth.Username.Trim(), PasswordHash = s.Auth.PasswordHash, IsAdmin = true, ApiKey = s.Auth.ApiKey,
            NotifyServices = ha.NotifyServices.Concat(string.IsNullOrWhiteSpace(ha.NotifyService) ? Array.Empty<string>() : new[] { ha.NotifyService }).Distinct().ToList(),
            CalendarEntities = new(ha.CalendarEntities), DefaultCalendarEntity = ha.DefaultCalendarEntity, TasksTodoEntity = ha.TasksTodoEntity,
            Channel = s.Notifications.Channel, TaskReminders = s.Notifications.TaskReminders,
            RenewalReminders = s.Notifications.RenewalReminders, ExpiryReminders = s.Notifications.ExpiryReminders,
            DailyDigest = s.Notifications.DailyDigest, DailyDigestTime = s.Notifications.DailyDigestTime,
            Schedule = Json.Clone(s.Schedule), Bible = Json.Clone(s.Bible),
            PersonEntity = ha.PresenceEntities.Count == 1 ? ha.PresenceEntities[0] : ""
        };
        if (string.IsNullOrEmpty(u.ApiKey)) u.ApiKey = PasswordHash.NewApiKey();
        s.Users.Add(u);
        foreach (var a in s.Email.Accounts) a.OwnerId = u.Id;
        s.Auth.Username = ""; s.Auth.PasswordHash = ""; s.Auth.ApiKey = "";
        ha.NotifyServices = new(); ha.NotifyService = ""; ha.CalendarEntities = new(); ha.DefaultCalendarEntity = ""; ha.TasksTodoEntity = "";
        // New people start with the Bible tracking sensors empty (they're someone's phone).
        s.Bible.AppSensors = new(); s.Bible.PlanEnabled = false;
        return true;
    }

    /// <summary>Tells the storage layer who's who and what's shared.</summary>
    private static void Publish(AppSettings s)
    {
        UserScope.Household = new HouseholdView(Primary(s)?.Id ?? Guid.Empty, s.Household.ShareKitchen, s.Household.ShareMeals,
            s.Household.ShareRenewals, s.Users.Select(u => u.Id).ToList());
    }

    private void Save(AppSettings s)
    {
        var toDisk = Json.Clone(s);
        WalkSecrets(toDisk, decrypt: false);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);
        var tmp = _paths.SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(toDisk, Json.Options));
        File.Move(tmp, _paths.SettingsFile, true);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile)) return new AppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile), Json.Options) ?? new();
            WalkSecrets(s, decrypt: true);
            // 1.2 upgrade: promotions are harmless, so file them automatically by default.
            if (!s.Ollama.PromotionAutoApplyMigrated)
            {
                if (!s.Ollama.AutoApplyTypes.Contains("promotion")) s.Ollama.AutoApplyTypes.Add("promotion");
                s.Ollama.PromotionAutoApplyMigrated = true;
            }
            // 1.2 upgrade: one notify service → list of phones.
            if (!string.IsNullOrWhiteSpace(s.HomeAssistant.NotifyService))
            {
                if (!s.HomeAssistant.NotifyServices.Contains(s.HomeAssistant.NotifyService)) s.HomeAssistant.NotifyServices.Add(s.HomeAssistant.NotifyService);
                s.HomeAssistant.NotifyService = "";
            }
            // 1.2 upgrade: kitchen stock from emails now waits for approval by default.
            if (!s.Ollama.PantryReviewMigrated)
            {
                s.Ollama.AutoApplyTypes.Remove("pantry");
                s.Ollama.PantryReviewMigrated = true;
            }
            return s;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "settings.json could not be read; starting with defaults (file left untouched)");
            File.Copy(_paths.SettingsFile, _paths.SettingsFile + ".broken", true);
            return new AppSettings();
        }
    }

    /// <summary>
    /// Apply a change to the latest settings and persist. Always mutate inside the callback
    /// so background jobs (e.g. email sync state) and the UI never overwrite each other.
    /// </summary>
    public async Task UpdateAsync(Action<AppSettings> mutate)
    {
        await _lock.WaitAsync();
        try
        {
            var userId = UserScope.UserId;
            var working = Json.Clone(_current);
            if (userId is { } uid) Overlay(working, uid);
            mutate(working);
            if (userId is { } owner) working = Split(working, _current, owner);
            EnsureUsers(working);
            var toDisk = Json.Clone(working);
            WalkSecrets(toDisk, decrypt: false);
            var tmp = _paths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(toDisk, Json.Options));
            File.Move(tmp, _paths.SettingsFile, true);
            _current = working;
            Publish(working);
        }
        finally { _lock.Release(); }
        using (UserScope.None())
            try { Changed?.Invoke(); } catch (Exception ex) { _log.LogWarning(ex, "Settings listener failed"); }
    }

    /// <summary>Changes a person's own profile (name, sign-in, PIN, API key…).</summary>
    public Task UpdateUserAsync(Guid id, Action<UserProfile> mutate) => UpdateAsync(s =>
    {
        var u = s.Users.FirstOrDefault(x => x.Id == id);
        if (u is not null) mutate(u);
    });

    private void WalkSecrets(object? obj, bool decrypt)
    {
        if (obj is null) return;
        var type = obj.GetType();
        if (type.IsPrimitive || obj is string || type.IsEnum) return;

        if (obj is IDictionary) return;
        if (obj is IEnumerable list)
        {
            foreach (var item in list) WalkSecrets(item, decrypt);
            return;
        }

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
            if (p.PropertyType == typeof(string) && p.GetCustomAttribute<SecretAttribute>() != null && p.CanWrite)
            {
                var v = (string?)p.GetValue(obj);
                if (string.IsNullOrEmpty(v)) continue;
                if (decrypt && v.StartsWith(Prefix))
                {
                    try { p.SetValue(obj, _protector.Unprotect(v[Prefix.Length..])); }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Could not decrypt {Prop}; key ring missing? Re-enter it in Settings.", p.Name);
                        p.SetValue(obj, "");
                    }
                }
                else if (!decrypt && !v.StartsWith(Prefix))
                {
                    p.SetValue(obj, Prefix + _protector.Protect(v));
                }
            }
            else if (!p.PropertyType.IsValueType && p.PropertyType != typeof(string))
            {
                WalkSecrets(p.GetValue(obj), decrypt);
            }
        }
    }
}
