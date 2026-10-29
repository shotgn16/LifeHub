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

    /// <summary>A signed-in LifeHub person. <paramref name="via"/> is "ingress" for the Home Assistant "Who's using LifeHub?" picker.</summary>
    public static ClaimsPrincipal Principal(UserProfile user, string scheme, string via = "password")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Name.Length > 0 ? user.Name : user.Username),
            new(UserScope.ClaimUserId, user.Id.ToString()),
            new(UserScope.ClaimVia, via)
        };
        if (user.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, AdminRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }
}

/// <summary>Authenticates REST API calls (e.g. from Home Assistant rest_command) with X-Api-Key or a Bearer token.</summary>
public sealed class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly SettingsService _settings;

    public ApiKeyAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, SettingsService settings) : base(options, logger, encoder) => _settings = settings;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? supplied = Request.Headers["X-Api-Key"].FirstOrDefault();
        var auth = Request.Headers.Authorization.FirstOrDefault();
        if (supplied is null && auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true) supplied = auth[7..].Trim();
        if (supplied is null) return Task.FromResult(AuthenticateResult.NoResult());

        // Each person has their own key, so a Shortcut or HA automation acts for that person.
        var bytes = System.Text.Encoding.UTF8.GetBytes(supplied);
        foreach (var user in _settings.Users)
        {
            if (string.IsNullOrEmpty(user.ApiKey)) continue;
            if (CryptographicOperations.FixedTimeEquals(bytes, System.Text.Encoding.UTF8.GetBytes(user.ApiKey)))
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(AuthConstants.Principal(user, Scheme.Name, "api"), Scheme.Name)));
        }
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
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
        var s = settings.Household.Auth;
        var fromIngress = ctx.Items.ContainsKey("ingress");
        var trusted = fromIngress && s.TrustHomeAssistantIngress;
        var path = ctx.Request.Path.Value ?? "";

        // A choice made on the HA "Who's using LifeHub?" screen only counts inside Home Assistant.
        if (ctx.User.FindFirst(UserScope.ClaimVia)?.Value == "ingress" && !trusted)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        // Someone who has been removed is signed out.
        if (ctx.User.Identity?.IsAuthenticated == true && UserScope.FromPrincipal(ctx.User) is null && !path.StartsWith("/api/"))
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());

        var users = settings.Users;
        if (trusted && ctx.User.Identity?.IsAuthenticated != true && users.Count == 1)
        {
            // One person: Home Assistant has already signed them in.
            var only = users[0];
            var haName = ctx.Request.Headers["X-Remote-User-Display-Name"].FirstOrDefault();
            if (only.Name == "Me" && !string.IsNullOrWhiteSpace(haName))
                await settings.UpdateAsync(x => { if (x.Users.Count == 1 && x.Users[0].Name == "Me") x.Users[0].Name = haName.Trim(); });
            ctx.User = AuthConstants.Principal(settings.Users[0], "Ingress", "ingress");
        }

        var exempt = path.StartsWith("/setup") || path.StartsWith("/auth/") || path.StartsWith("/_framework") || path.StartsWith("/_blazor") ||
                     path.StartsWith("/who") || path.StartsWith("/api/") || path.StartsWith("/health") || Path.HasExtension(path);
        if (trusted && ctx.User.Identity?.IsAuthenticated != true && users.Count > 1 && !exempt)
        {
            // Several people: ask who it is (HA's own sign-in doesn't tell LifeHub reliably who's holding the phone).
            var back = ctx.Request.PathBase + ctx.Request.Path + ctx.Request.QueryString;
            ctx.Response.Redirect(ctx.Request.PathBase + "/who?returnUrl=" + Uri.EscapeDataString(back));
            return;
        }
        if (!settings.HasLogin && !trusted && !exempt)
        {
            ctx.Response.Redirect(ctx.Request.PathBase + "/setup");
            return;
        }
        await _next(ctx);
    }
}

/// <summary>Runs after authorization: everything this request does (pages, API calls) happens as the signed-in person.</summary>
public sealed class UserScopeMiddleware
{
    private readonly RequestDelegate _next;
    public UserScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var id = UserScope.FromPrincipal(ctx.User);
        using (id is { } uid ? UserScope.As(uid) : UserScope.None())
            await _next(ctx);
    }
}

/// <summary>
/// Blazor circuit: every event, render and JS call runs as the person signed in to that browser tab.
/// </summary>
public sealed class UserCircuitHandler : Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler
{
    private readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider _auth;
    public UserCircuitHandler(Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider auth) => _auth = auth;

    public override Func<Microsoft.AspNetCore.Components.Server.Circuits.CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<Microsoft.AspNetCore.Components.Server.Circuits.CircuitInboundActivityContext, Task> next) => async context =>
    {
        Guid? id = null;
        try { id = UserScope.FromPrincipal((await _auth.GetAuthenticationStateAsync()).User); } catch (InvalidOperationException) { }
        using (id is { } uid ? UserScope.As(uid) : UserScope.None())
            await next(context);
    };
}

/// <summary>The person signed in to this browser tab (for pages and components).</summary>
public sealed class UserContext
{
    private readonly SettingsService _settings;
    private readonly Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider _auth;
    private Guid? _id;

    public UserContext(SettingsService settings, Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider auth)
    {
        _settings = settings; _auth = auth;
    }

    public Guid Id => _id ??= UserScope.UserId ?? Resolve() ?? Guid.Empty;

    private Guid? Resolve()
    {
        try
        {
            var t = _auth.GetAuthenticationStateAsync();
            return t.IsCompletedSuccessfully ? UserScope.FromPrincipal(t.Result.User) : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    public UserProfile? Me => _settings.User(Id);
    public bool IsAdmin => Me?.IsAdmin == true;
    public IReadOnlyList<UserProfile> Everyone => _settings.Users;
    public bool Multi => _settings.Users.Count > 1;

    /// <summary>Runs code as this tab's person — for timers and change notifications arriving from elsewhere.</summary>
    public IDisposable Scope() => Id == Guid.Empty ? UserScope.None() : UserScope.As(Id);
}
