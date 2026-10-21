using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LifeHub.Services;

/// <summary>
/// LifeHub's own push notifications (the standard Web Push protocol used by Chrome, Edge, Firefox and Safari,
/// including iPhone home-screen apps). Messages are encrypted end-to-end (RFC 8291) and signed with this
/// server's VAPID key (RFC 8292) — no third-party account needed.
/// </summary>
public sealed class WebPushService
{
    private readonly IRepository<PushDevice> _devices;
    private readonly SettingsService _settings;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WebPushService> _log;
    private readonly SemaphoreSlim _keyLock = new(1, 1);

    public WebPushService(IRepository<PushDevice> devices, SettingsService settings, IHttpClientFactory http, ILogger<WebPushService> log)
    {
        _devices = devices; _settings = settings; _http = http; _log = log;
    }

    public IRepository<PushDevice> Devices => _devices;

    /// <summary>The public key browsers need to subscribe (base64url). Created on first use.</summary>
    public async Task<string> PublicKeyAsync()
    {
        var n = _settings.Current.Notifications;
        if (!string.IsNullOrEmpty(n.VapidPublicKey) && !string.IsNullOrEmpty(n.VapidPrivateKey)) return n.VapidPublicKey;
        await _keyLock.WaitAsync();
        try
        {
            n = _settings.Current.Notifications;
            if (!string.IsNullOrEmpty(n.VapidPublicKey) && !string.IsNullOrEmpty(n.VapidPrivateKey)) return n.VapidPublicKey;
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = key.ExportParameters(true);
            var pub = B64(Uncompressed(p.Q));
            var priv = B64(p.D!);
            await _settings.UpdateAsync(s => { s.Notifications.VapidPublicKey = pub; s.Notifications.VapidPrivateKey = priv; });
            return pub;
        }
        finally { _keyLock.Release(); }
    }

    public async Task<PushDevice> SaveDeviceAsync(string endpoint, string p256dh, string auth, string name, string userAgent)
    {
        var existing = _devices.Where(d => d.Endpoint == endpoint).FirstOrDefault();
        var device = existing ?? new PushDevice { Endpoint = endpoint };
        device.P256dh = p256dh;
        device.Auth = auth;
        if (!string.IsNullOrWhiteSpace(name)) device.Name = name;
        device.UserAgent = userAgent;
        device.LastError = "";
        await _devices.UpsertAsync(device);
        return device;
    }

    /// <summary>Sends to every registered device (or only those of <paramref name="person"/> when any are linked to them).</summary>
    public async Task<int> SendAsync(string title, string body, string? url = null, string? tag = null, string? person = null)
    {
        var devices = _devices.All().ToList();
        if (!string.IsNullOrEmpty(person))
        {
            var mine = devices.Where(d => d.Person == person).ToList();
            if (mine.Count > 0) devices = mine;
        }
        var sent = 0;
        foreach (var d in devices)
            if (await SendToAsync(d, title, body, url, tag)) sent++;
        return sent;
    }

    public async Task<bool> SendToAsync(PushDevice device, string title, string body, string? url, string? tag)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { title, body, url = url ?? "", tag = tag ?? "" });
            var encrypted = Encrypt(payload, device.P256dh, device.Auth);
            using var req = new HttpRequestMessage(HttpMethod.Post, device.Endpoint) { Content = new ByteArrayContent(encrypted) };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            req.Content.Headers.ContentEncoding.Add("aes128gcm");
            req.Headers.Add("TTL", "86400");
            req.Headers.Add("Urgency", "normal");
            if (!string.IsNullOrEmpty(tag))
            {
                // Same topic replaces an undelivered older message (e.g. two "use up soon" notices while the phone was off).
                var topic = new string(("lifehub-" + tag).Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(32).ToArray());
                req.Headers.Add("Topic", topic);
            }
            var publicKey = await PublicKeyAsync();   // creates the key pair on first use
            var n = _settings.Current.Notifications;
            var origin = new Uri(device.Endpoint).GetLeftPart(UriPartial.Authority);
            req.Headers.TryAddWithoutValidation("Authorization", $"vapid t={VapidJwt(origin, n)}, k={publicKey}");

            var client = _http.CreateClient("webpush");
            client.Timeout = TimeSpan.FromSeconds(20);
            using var res = await client.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                device.LastOkUtc = DateTime.UtcNow;
                device.LastError = "";
                await _devices.UpsertAsync(device);
                return true;
            }
            var text = await res.Content.ReadAsStringAsync();
            if (res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                _log.LogInformation("Push subscription for {Device} expired; removing it", device.Name);
                await _devices.DeleteAsync(device.Id);
                return false;
            }
            device.LastError = $"{(int)res.StatusCode} {text[..Math.Min(160, text.Length)]}";
            await _devices.UpsertAsync(device);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Web push to {Device} failed", device.Name);
            device.LastError = ex.Message;
            await _devices.UpsertAsync(device);
            return false;
        }
    }

    // ------------------------------------------------------------------ VAPID (RFC 8292)

    private string VapidJwt(string audience, NotificationSettings n)
    {
        var subject = !string.IsNullOrWhiteSpace(n.VapidSubject) ? n.VapidSubject
            : _settings.Current.General.PublicUrl is { Length: > 0 } pu && pu.StartsWith("https://") ? pu.TrimEnd('/')
            : "mailto:lifehub@example.com";
        var header = B64(Encoding.UTF8.GetBytes("{\"typ\":\"JWT\",\"alg\":\"ES256\"}"));
        var exp = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
        var claims = B64(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["aud"] = audience, ["exp"] = exp, ["sub"] = subject }));
        var signingInput = Encoding.ASCII.GetBytes($"{header}.{claims}");
        var pub = UnB64(n.VapidPublicKey);
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = UnB64(n.VapidPrivateKey),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] }
        });
        // .NET signs in IEEE P1363 format (r||s), which is what JWS ES256 expects.
        var sig = key.SignData(signingInput, HashAlgorithmName.SHA256);
        return $"{header}.{claims}.{B64(sig)}";
    }

    // ------------------------------------------------------------------ message encryption (RFC 8291 / RFC 8188)

    public static byte[] Encrypt(byte[] plaintext, string p256dh, string auth)
    {
        var uaPublic = UnB64(p256dh);
        var authSecret = UnB64(auth);
        if (uaPublic.Length != 65 || uaPublic[0] != 0x04) throw new InvalidOperationException("Bad subscription key");

        using var server = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var asPublic = Uncompressed(server.ExportParameters(false).Q);
        using var ua = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = uaPublic[1..33], Y = uaPublic[33..65] }
        });
        var ecdhSecret = server.DeriveRawSecretAgreement(ua.PublicKey);

        // IKM = HKDF(auth_secret, ecdh_secret, "WebPush: info" || 0x00 || ua_public || as_public, 32)
        var keyInfo = Concat(Encoding.ASCII.GetBytes("WebPush: info\0"), uaPublic, asPublic);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, ecdhSecret, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, keyInfo);

        var salt = RandomNumberGenerator.GetBytes(16);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var padded = Concat(plaintext, new byte[] { 0x02 });   // last (only) record delimiter, no padding
        var cipher = new byte[padded.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, padded, cipher, tag);

        // Header: salt(16) | record size (uint32) | key id length (1) | key id (= as_public)
        var header = new byte[16 + 4 + 1 + asPublic.Length];
        salt.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), 4096);
        header[20] = (byte)asPublic.Length;
        asPublic.CopyTo(header, 21);
        return Concat(header, cipher, tag);
    }

    private static byte[] Uncompressed(ECPoint q) => Concat(new byte[] { 0x04 }, Pad32(q.X!), Pad32(q.Y!));
    private static byte[] Pad32(byte[] b) => b.Length == 32 ? b : Concat(new byte[32 - b.Length], b);

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var o = 0;
        foreach (var p in parts) { p.CopyTo(result, o); o += p.Length; }
        return result;
    }

    public static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] UnB64(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
