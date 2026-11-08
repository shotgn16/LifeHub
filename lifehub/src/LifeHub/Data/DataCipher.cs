using System.Security.Cryptography;
using System.Text;

namespace LifeHub.Data;

/// <summary>Where the data key is kept: a protected key file on computers and servers, the phone's secure storage in the apps.</summary>
public interface IDataKeyStore
{
    /// <summary>The 32-byte key, or null if none has been made yet.</summary>
    byte[]? Load();
    void Save(byte[] key);
    /// <summary>A recovery key someone put in place (e.g. after moving the data to a new computer), or null.</summary>
    byte[]? TakeRecoveryKey();
}

/// <summary>
/// Encrypts everything LifeHub stores — its database files and settings — with AES-256-GCM.
/// <code>
/// file = "LHE1" | nonce (12 bytes) | tag (16 bytes) | ciphertext
/// </code>
/// Files without the "LHE1" header are read as plain JSON (LifeHub 3.0 and earlier) and encrypted the next
/// time they are saved; <see cref="EncryptExisting"/> does all of them at start-up.
/// </summary>
public sealed class DataCipher
{
    private static readonly byte[] Magic = "LHE1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16, HeaderSize = 4 + NonceSize + TagSize;
    private readonly byte[] _key;

    public DataCipher(byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("The data key must be 32 bytes", nameof(key));
        _key = key;
    }

    /// <summary>Loads the key (or adopts a recovery key, or makes a new one when there is nothing encrypted yet).</summary>
    public static DataCipher Open(IDataKeyStore store, Func<bool> anyEncrypted)
    {
        var key = store.Load();
        if (key is null && store.TakeRecoveryKey() is { } recovered)
        {
            store.Save(recovered);
            key = recovered;
        }
        if (key is null)
        {
            if (anyEncrypted())
                throw new InvalidOperationException(
                    "LifeHub's data is encrypted but its key is missing. Put your recovery key (Settings → Backup) in keys/recovery.key " +
                    "in the data folder, or set LIFEHUB_DATA_KEY, then start LifeHub again.");
            key = RandomNumberGenerator.GetBytes(32);
            store.Save(key);
        }
        return new DataCipher(key);
    }

    /// <summary>The key as text, to keep somewhere safe: it opens backups on another computer.</summary>
    public string RecoveryKey => Convert.ToBase64String(_key);

    public static byte[]? ParseRecoveryKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException) { return null; }
    }

    public static bool IsEncrypted(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[..4].SequenceEqual(Magic);

    public static bool IsEncryptedFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return fs.Read(head) == 4 && head.SequenceEqual(Magic);
        }
        catch { return false; }
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plain)
    {
        var output = new byte[HeaderSize + plain.Length];
        Magic.CopyTo(output, 0);
        var nonce = output.AsSpan(4, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderSize), output.AsSpan(4 + NonceSize, TagSize), Magic);
        return output;
    }

    /// <summary>Decrypts LifeHub's format; anything else is returned as it is (plain files from before encryption).</summary>
    public byte[] Decrypt(byte[] data)
    {
        if (!IsEncrypted(data)) return data;
        var plain = new byte[data.Length - HeaderSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(data.AsSpan(4, NonceSize), data.AsSpan(HeaderSize), data.AsSpan(4 + NonceSize, TagSize), plain, Magic);
        return plain;
    }

    public byte[] ReadFile(string path) => Decrypt(File.ReadAllBytes(path));
    public string ReadText(string path) => Encoding.UTF8.GetString(ReadFile(path));

    /// <summary>Writes encrypted, atomically (via a temporary file), so a crash never leaves half a file.</summary>
    public void WriteFile(string path, ReadOnlySpan<byte> plain)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Encrypt(plain));
        File.Move(tmp, path, overwrite: true);
    }

    public void WriteText(string path, string text) => WriteFile(path, Encoding.UTF8.GetBytes(text));

    public async Task WriteFileAsync(string path, byte[] plain)
    {
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, Encrypt(plain));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Encrypts every plain file among these (upgrading from LifeHub 3.0). Returns how many.</summary>
    public int EncryptExisting(IEnumerable<string> files)
    {
        var n = 0;
        foreach (var f in files)
        {
            if (!File.Exists(f) || IsEncryptedFile(f)) continue;
            WriteFile(f, File.ReadAllBytes(f));
            n++;
        }
        return n;
    }
}
