using System.Security.Claims;

namespace SlimeGoals.Server.Auth;

/// <summary>A conta e o aparelho vêm só do token da sessão, nunca de parâmetro do cliente (regra 6).</summary>
public static class CurrentDevice
{
    public const string AccountClaim = "slimegoals:account_id";
    public const string DeviceClaim = "slimegoals:device_id";

    public static Guid AccountId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(AccountClaim)!);

    public static Guid DeviceId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(DeviceClaim)!);
}
