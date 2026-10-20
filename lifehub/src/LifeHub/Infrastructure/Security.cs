using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LifeHub.Infrastructure;

public static class PasswordHash
{
    private const int Iterations = 210_000;

    public static string Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        var iterations = int.Parse(parts[1]);
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static string NewApiKey() => "lh_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
}

/// <summary>Very small brute-force guard for the login form.</summary>
public sealed class LoginThrottle
{
    private readonly ConcurrentDictionary<string, (int Failures, DateTime Until)> _state = new();

    public bool IsLocked(string key) => _state.TryGetValue(key, out var s) && s.Until > DateTime.UtcNow;

    public void Fail(string key)
    {
        var s = _state.GetOrAdd(key, _ => (0, DateTime.MinValue));
        var failures = s.Failures + 1;
        var until = failures >= 5 ? DateTime.UtcNow.AddMinutes(Math.Min(30, failures)) : DateTime.MinValue;
        _state[key] = (failures, until);
    }

    public void Success(string key) => _state.TryRemove(key, out _);
}

public static class AuthConstants
{
    public const string ApiKeyScheme = "ApiKey";
    public const string AdminRole = "admin";

    public static ClaimsPrincipal Principal(string name, string scheme) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, AdminRole) }, scheme));
}

/// <summary>Authenticates REST API calls (e.g. from Home Assistant rest_command) with X-Api-Key or a Bearer token.</summary>
public sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly SettingsService _settings;

    public ApiKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, SettingsService settings) : base(options, logger, encoder) => _settings = settings;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var key = _settings.Current.Auth.ApiKey;
        if (string.IsNullOrEmpty(key)) return Task.FromResult(AuthenticateResult.NoResult());

        string? supplied = Request.Headers["X-Api-Key"].FirstOrDefault();
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (supplied is null && auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true) supplied = auth[7..].Trim();
        if (supplied is null) return Task.FromResult(AuthenticateResult.NoResult());

        var ok = CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(key));
        return Task.FromResult(ok
            ? AuthenticateResult.Success(new AuthenticationTicket(AuthConstants.Principal("api", Scheme.Name), Scheme.Name))
            : AuthenticateResult.Fail("Invalid API key"));
    }
}

/// <summary>
/// Home Assistant ingress support: honours X-Ingress-Path as the path base and, when enabled,
/// treats requests coming from the Supervisor ingress proxy (172.30.32.2) as signed in,
/// because HA has already authenticated the user.
/// </summary>
public sealed class IngressMiddleware
{
    // The Supervisor's ingress proxy address (overridable for testing behind your own proxy).
    private static readonly IPAddress IngressProxy =
        IPAddress.Parse(Environment.GetEnvironmentVariable("LIFEHUB_INGRESS_PROXY") ?? "172.30.32.2");
    private readonly RequestDelegate _next;

    public IngressMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, DataPaths paths, SettingsService settings, ILogger<IngressMiddleware> log)
    {
        var ingressPath = ctx.Request.Headers["X-Ingress-Path"].FirstOrDefault();
        if (paths.IsHomeAssistantAddon && !string.IsNullOrEmpty(ingressPath) && ingressPath.StartsWith("/api/hassio_ingress/"))
        {
            // Always keep links and redirects inside the ingress URL, so nothing escapes to HA's own 404.
            ctx.Request.PathBase = new PathString(ingressPath.TrimEnd('/'));
            // Only trust HA's sign-in when the connection really comes from the Supervisor. This runs before
            // forwarded-header processing, so RemoteIpAddress is the actual proxy, not the browser.
            if (IsIngressProxy(ctx.Connection.RemoteIpAddress)) ctx.Items["ingress"] = true;
            else if (!_warned)
            {
                _warned = true;
                log.LogWarning("Ingress request from {Ip}, not the Supervisor ({Proxy}); HA sign-in not trusted for it.", ctx.Connection.RemoteIpAddress, IngressProxy);
            }
        }
        await _next(ctx);
    }

    private static bool _warned;

    internal static bool IsIngressProxy(IPAddress? ip) =>
        ip is not null && (ip.Equals(IngressProxy) || (ip.IsIPv4MappedToIPv6 && ip.MapToIPv4().Equals(IngressProxy)));
}

/// <summary>Runs after authentication: signs ingress users in, forces first-run setup.</summary>
public sealed class SetupAndIngressAuthMiddleware
{
    private readonly RequestDelegate _next;
    public SetupAndIngressAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, SettingsService settings)
    {
        var s = settings.Current.Auth;
        var fromIngress = ctx.Items.ContainsKey("ingress");

        if (fromIngress && s.TrustHomeAssistantIngress && ctx.User.Identity?.IsAuthenticated != true)
        {
            var name = ctx.Request.Headers["X-Remote-User-Display-Name"].FirstOrDefault()
                       ?? ctx.Request.Headers["X-Remote-User-Name"].FirstOrDefault() ?? "Home Assistant";
            ctx.User = AuthConstants.Principal(name, "Ingress");
        }

        var path = ctx.Request.Path.Value ?? "";
        var exempt = path.StartsWith("/setup") || path.StartsWith("/auth/") || path.StartsWith("/_framework") ||
                     path.StartsWith("/api/") || path.StartsWith("/health") || Path.HasExtension(path);
        if (!s.IsConfigured && !(fromIngress && s.TrustHomeAssistantIngress) && !exempt)
        {
            ctx.Response.Redirect(ctx.Request.PathBase + "/setup");
            return;
        }
        await _next(ctx);
    }
}
