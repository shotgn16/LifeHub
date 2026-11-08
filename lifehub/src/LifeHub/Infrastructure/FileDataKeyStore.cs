using Microsoft.AspNetCore.DataProtection;

namespace LifeHub.Infrastructure;

/// <summary>
/// Keeps the data key in <c>keys/data.key</c>, itself encrypted with ASP.NET Core Data Protection (whose keys are
/// protected by Windows DPAPI on Windows) and readable only by LifeHub's user on macOS and Linux.
/// <c>LIFEHUB_DATA_KEY</c> (the recovery key) can supply the key instead, e.g. to keep it out of a Docker volume.
/// </summary>
public sealed class FileDataKeyStore : IDataKeyStore
{
    private readonly DataPaths _paths;
    private readonly IDataProtector _protector;

    public FileDataKeyStore(DataPaths paths, IDataProtectionProvider dp)
    {
        _paths = paths;
        _protector = dp.CreateProtector("LifeHub.DataKey.v1");
    }

    private string KeyFile => Path.Combine(_paths.Keys, "data.key");
    private string RecoveryFile => Path.Combine(_paths.Keys, "recovery.key");

    public byte[]? Load()
    {
        if (DataCipher.ParseRecoveryKey(Environment.GetEnvironmentVariable("LIFEHUB_DATA_KEY")) is { } fromEnv) return fromEnv;
        if (!File.Exists(KeyFile)) return null;
        return DataCipher.ParseRecoveryKey(_protector.Unprotect(File.ReadAllText(KeyFile).Trim()))
               ?? throw new InvalidOperationException("keys/data.key is damaged");
    }

    public void Save(byte[] key)
    {
        if (Environment.GetEnvironmentVariable("LIFEHUB_DATA_KEY") is { Length: > 0 }) return;   // supplied from outside
        Directory.CreateDirectory(_paths.Keys);
        File.WriteAllText(KeyFile, _protector.Protect(Convert.ToBase64String(key)));
        LockDown(KeyFile);
    }

    public byte[]? TakeRecoveryKey()
    {
        if (!File.Exists(RecoveryFile)) return null;
        var key = DataCipher.ParseRecoveryKey(File.ReadAllText(RecoveryFile));
        if (key is not null) File.Delete(RecoveryFile);
        return key;
    }

    /// <summary>Only LifeHub's own user may read the keys folder (macOS, Linux, Docker).</summary>
    public static void LockDown(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            if (Directory.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}
