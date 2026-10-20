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

    /// <summary>Performs the action. Returns a short result line shown in the AI Inbox.</summary>
    Task<string> ApplyAsync(AiSuggestion suggestion, CancellationToken ct);
}
