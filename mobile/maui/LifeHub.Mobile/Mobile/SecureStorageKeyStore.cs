namespace LifeHub.Mobile.Mobile;

/// <summary>The data key lives in the phone's secure storage (Android Keystore / iOS Keychain), never in a file.</summary>
public sealed class SecureStorageKeyStore : IDataKeyStore
{
    private const string Name = "lifehub-data-key";
    private readonly DataPaths _paths;

    public SecureStorageKeyStore(DataPaths paths) => _paths = paths;

    private static string? Get()
    {
        try { return Task.Run(() => SecureStorage.Default.GetAsync(Name)).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    public static bool HasKey() => !string.IsNullOrEmpty(Get());

    public byte[]? Load() => DataCipher.ParseRecoveryKey(Get());

    public void Save(byte[] key) => Task.Run(() => SecureStorage.Default.SetAsync(Name, Convert.ToBase64String(key))).GetAwaiter().GetResult();

    public byte[]? TakeRecoveryKey()
    {
        var f = Path.Combine(_paths.Keys, "recovery.key");
        if (!File.Exists(f)) return null;
        var key = DataCipher.ParseRecoveryKey(File.ReadAllText(f));
        if (key is not null) File.Delete(f);
        return key;
    }
}
