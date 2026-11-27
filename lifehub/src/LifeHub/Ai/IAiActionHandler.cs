namespace LifeHub.Ai;

/// <summary>
/// One thing the AI is allowed to do. To add a new capability (e.g. "turn on a scene",
/// "log a parcel delivery"), implement this interface and register it in Program.cs with
/// <c>builder.Services.AddSingleton&lt;IAiActionHandler, YourHandler&gt;()</c>.
/// The handler's Type and Instructions are automatically added to the model's prompt and
/// JSON schema, and it appears in Settings → AI for auto-apply.
/// </summary>
public interface IAiActionHandler
{
    /// <summary>Short lowercase id the model returns, e.g. "task".</summary>
    string Type { get; }

    /// <summary>Human label for the UI.</summary>
    string Label { get; }

    /// <summary>
    /// Tells the model when to use this action and how to fill the common fields
    /// (title, description, date, time, duration_minutes, category, priority, amount, currency).
    /// </summary>
    string Instructions { get; }

    /// <summary>The module this action belongs to (Settings → Modules); while it's switched off the AI isn't offered it.</summary>
    string? Module => null;

    /// <summary>Performs the action. Returns a short result line shown in the AI Inbox.</summary>
    Task<string> ApplyAsync(AiSuggestion suggestion, CancellationToken ct);
}

/// <summary>
/// An action only for things the user types or says (Ask LifeHub, quick capture, Telegram) — never offered
/// when the AI reads an email, because an email's sender shouldn't be able to change your schedule or memory.
/// </summary>
public interface IUserOnlyAction { }
