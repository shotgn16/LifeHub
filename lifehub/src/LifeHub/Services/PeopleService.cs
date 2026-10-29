namespace LifeHub.Services;

/// <summary>Adding and removing the people who use LifeHub.</summary>
public sealed class PeopleService
{
    private readonly SettingsService _settings;
    private readonly IServiceProvider _sp;
    private readonly ILogger<PeopleService> _log;

    public static readonly string[] Colours = { "#0f766e", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444", "#ec4899", "#8b5cf6", "#14b8a6" };

    public PeopleService(SettingsService settings, IServiceProvider sp, ILogger<PeopleService> log)
    {
        _settings = settings; _sp = sp; _log = log;
    }

    /// <summary>A new person starts from the household's default schedule and Bible settings, with their own API key.</summary>
    public async Task<UserProfile> AddAsync(string name, bool admin, string personEntity = "", string username = "", string password = "")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a name");
        var users = _settings.Users;
        if (username.Length > 0 && users.Any(u => u.Username.Equals(username.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Someone already uses that username");
        if (password.Length is > 0 and < 8) throw new InvalidOperationException("Passwords need at least 8 characters");
        var household = _settings.Household;
        var user = new UserProfile
        {
            Name = name.Trim(), IsAdmin = admin, PersonEntity = personEntity,
            Colour = Colours[users.Count % Colours.Length],
            Username = username.Trim(), PasswordHash = password.Length > 0 ? PasswordHash.Create(password) : "",
            ApiKey = PasswordHash.NewApiKey(),
            Schedule = Json.Clone(household.Schedule), Bible = Json.Clone(household.Bible)
        };
        user.Bible.AppSensors = new();
        await _settings.UpdateAsync(s => s.Users.Add(user));
        return user;
    }

    /// <summary>
    /// Removes a person: their tasks, emails, mailboxes, Bible history, studies and notes are deleted; anything they
    /// added to the kitchen, meals or renewals stays with the household.
    /// </summary>
    public async Task<int> RemoveAsync(Guid id)
    {
        var users = _settings.Users;
        var user = users.FirstOrDefault(u => u.Id == id) ?? throw new InvalidOperationException("Not found");
        if (user.IsAdmin && users.Count(u => u.IsAdmin) == 1) throw new InvalidOperationException("Make someone else an admin first");
        var wasPrimary = _settings.PrimaryUserId == id;
        var newPrimary = users.First(u => u.Id != id && (u.IsAdmin || !users.Any(x => x.Id != id && x.IsAdmin))).Id;

        int removed = 0;
        using (UserScope.AsSystem())
        {
            foreach (var type in typeof(Entity).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Entity)) && !t.IsAbstract))
            {
                if (_sp.GetService(typeof(IRepository<>).MakeGenericType(type)) is not IOwnedRepository repo) continue;
                removed += await repo.ForgetOwnerAsync(id, includeUnowned: wasPrimary);
            }
            await _settings.UpdateAsync(s =>
            {
                s.Users.RemoveAll(u => u.Id == id);
                s.Email.Accounts.RemoveAll(a => a.OwnerId == id || (wasPrimary && a.OwnerId == Guid.Empty));
                // Move the next main person to the front so household data (no owner) belongs to them.
                var next = s.Users.FirstOrDefault(u => u.Id == newPrimary);
                if (next is not null) { s.Users.Remove(next); s.Users.Insert(0, next); next.IsAdmin = true; }
            });
        }
        _log.LogInformation("Removed {Name}: {Count} personal records", user.Name, removed);
        return removed;
    }
}
