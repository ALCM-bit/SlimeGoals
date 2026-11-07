using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Auth;

/// <summary>
/// Autentica o <c>Authorization: Bearer</c> consultando <c>devices</c> a cada pedido, sem cache,
/// para que revogar valha na hora (decisão 2).
/// </summary>
public sealed class DeviceTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AccountStore store,
    TimeProvider time) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceToken";

    private const string Prefix = "Bearer ";
    private const int MaxTokenLength = 256;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[Prefix.Length..].Trim();
        if (token.Length is 0 or > MaxTokenLength)
        {
            return AuthenticateResult.Fail("token inválido");
        }

        var device = await store.FindActiveDeviceAsync(SecretHash.Of(token), time.GetUtcNow(), Context.RequestAborted);
        if (device is null)
        {
            return AuthenticateResult.Fail("token desconhecido ou revogado");
        }

        var identity = new ClaimsIdentity(
            [new Claim(CurrentDevice.AccountClaim, device.AccountId.ToString()), new Claim(CurrentDevice.DeviceClaim, device.DeviceId.ToString())],
            SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new ErrorResponse("unauthorized"), SyncJsonContext.Default.ErrorResponse);
    }
}
