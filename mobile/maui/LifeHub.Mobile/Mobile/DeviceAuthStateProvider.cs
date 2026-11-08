using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace LifeHub.Mobile.Mobile;

/// <summary>On its own, the phone app is one person's LifeHub: always "signed in" as them, no password.</summary>
public sealed class DeviceAuthStateProvider : AuthenticationStateProvider
{
    private readonly SettingsService _settings;
    public DeviceAuthStateProvider(SettingsService settings) => _settings = settings;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var me = _settings.User(_settings.PrimaryUserId);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, me?.Name ?? "Me"),
            new Claim(UserScope.ClaimUserId, _settings.PrimaryUserId.ToString()),
            new Claim(UserScope.ClaimVia, "device")
        }, "device");
        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }
}
