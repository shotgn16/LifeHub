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
    }

    /// <summary>A copy of the current settings (decrypted). Edit it, then pass to UpdateAsync.</summary>
    public AppSettings Current => Json.Clone(_current);

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
            var working = Json.Clone(_current);
            mutate(working);
            var toDisk = Json.Clone(working);
            WalkSecrets(toDisk, decrypt: false);
            var tmp = _paths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(toDisk, Json.Options));
            File.Move(tmp, _paths.SettingsFile, true);
            _current = working;
        }
        finally { _lock.Release(); }
        try { Changed?.Invoke(); } catch (Exception ex) { _log.LogWarning(ex, "Settings listener failed"); }
    }

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
