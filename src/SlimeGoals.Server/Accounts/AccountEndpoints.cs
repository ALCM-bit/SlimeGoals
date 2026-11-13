using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using SlimeGoals.Server.Auth;
using SlimeGoals.Server.Limits;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Accounts;

/// <summary>Rotas de cadastro, entrada e saída: <c>/auth/*</c>.</summary>
public static class AccountEndpoints
{
    private const int KeyBytes = 32;
    private const int MaxKdfParamsLength = 256;
    private const int MaxWrappedKeyBytes = 256;

    // Hash sem dono: o login de e-mail desconhecido compara contra ele, para o caminho ser parecido.
    private static readonly byte[] DummyHash = SecretHash.Of(new byte[KeyBytes]);

    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth").WithIpRateLimit();
        auth.MapPost("/prelogin", PreloginAsync);
        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/confirm", ConfirmAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
        auth.MapPost("/password", ChangePasswordAsync).RequireAuthorization();
        auth.MapPost("/recover/begin", RecoverBeginAsync);
        auth.MapPost("/recover", RecoverAsync);

        routes.MapGet("/devices", ListDevicesAsync).RequireAuthorization();
        routes.MapDelete("/devices/{id:guid}", RevokeDeviceAsync).RequireAuthorization();
        routes.MapDelete("/account", DeleteAccountAsync).RequireAuthorization();
    }

    /// <summary>A regra é a de <see cref="AccountRules.NormalizeEmail"/>, a mesma que o app aplica antes de enviar.</summary>
    public static string? NormalizeEmail(string? email) => AccountRules.NormalizeEmail(email);

    internal static bool HasControl(string text) => text.Any(char.IsControl);

    private static IResult Error(string code, int status) =>
        Results.Json(new ErrorResponse(code), SyncJsonContext.Default.ErrorResponse, statusCode: status);

    internal static IResult ResetRequired() => Error("reset_required", StatusCodes.Status410Gone);

    internal static IResult InvalidRequest() => Error("invalid_request", StatusCodes.Status400BadRequest);

    internal static async Task<T?> ReadBodyAsync<T>(HttpRequest request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await request.ReadFromJsonAsync(typeInfo, cancellationToken);
        }
        // Corpo grande demais (413) sobe até o middleware de limites; as demais falhas de leitura viram 400.
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException { StatusCode: not StatusCodes.Status413PayloadTooLarge })
        {
            return null;
        }
    }

    private static async Task<IResult> PreloginAsync(
        HttpRequest request, AccountStore store, AuthRateLimits limits, IOptions<ServerOptions> options, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.PreloginRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        if (email is null)
        {
            return InvalidRequest();
        }

        if (limits.HitEmail(email) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var serverOptions = options.Value.Server;
        var kdf = await store.FindKdfAsync(email, cancellationToken);
        if (kdf is not null)
        {
            return Results.Json(new PreloginResponse(kdf.KdfSalt, kdf.KdfParams), SyncJsonContext.Default.PreloginResponse);
        }

        // Sal falso e estável: quem pergunta não descobre quem tem conta (decisão 14).
        var fake = HMACSHA256.HashData(Encoding.UTF8.GetBytes(serverOptions.PreloginSecret), Encoding.UTF8.GetBytes(email))[..16];
        return Results.Json(new PreloginResponse(fake, serverOptions.DefaultKdfParams), SyncJsonContext.Default.PreloginResponse);
    }

    private static async Task<IResult> RegisterAsync(
        HttpRequest request,
        AccountStore store,
        AuthRateLimits limits,
        IEmailSender emailSender,
        IOptions<ServerOptions> options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.RegisterRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        if (body is null || email is null || !IsValid(body))
        {
            return InvalidRequest();
        }

        if (limits.HitEmail(email) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var now = time.GetUtcNow();
        var token = SecretHash.NewToken();
        var tokenHash = SecretHash.Of(token);
        var sendEmail = await store.TryCreateOrReplacePendingAsync(
            new NewAccount(
                email,
                body.KdfSalt!,
                body.KdfParams!,
                SecretHash.Of(body.AuthKey!),
                SecretHash.Of(body.RecoveryKey!),
                body.WrappedKeyPassword!,
                body.WrappedKeyRecovery!,
                tokenHash,
                now.AddHours(options.Value.Retention.UnconfirmedAccountHours),
                now),
            cancellationToken);

        // Mesma resposta para e-mail novo, pendente e confirmado (decisão 6). A conta nova ou substituída manda
        // e-mail; a confirmada fica como está e não manda nada.
        if (sendEmail)
        {
            try
            {
                // Sem o token do pedido: desconectar o cliente não pode deixar conta sem e-mail.
                await emailSender.SendConfirmationAsync(email, token, CancellationToken.None);
            }
            catch
            {
                await store.DeleteUnconfirmedAsync(tokenHash, CancellationToken.None);
                throw;
            }
        }

        return Results.Accepted();
    }

    private static bool IsValid(RegisterRequest body) =>
        IsValidKeyMaterial(body.KdfSalt, body.KdfParams, body.AuthKey, body.WrappedKeyPassword)
        && body.RecoveryKey is { Length: KeyBytes }
        && body.WrappedKeyRecovery is { Length: > 0 and <= MaxWrappedKeyBytes };

    private static bool IsValidKeyMaterial(byte[]? kdfSalt, string? kdfParams, byte[]? authKey, byte[]? wrappedKeyPassword) =>
        kdfSalt is { Length: >= 16 and <= 64 }
        && !string.IsNullOrWhiteSpace(kdfParams) && kdfParams.Length <= MaxKdfParamsLength
        && !HasControl(kdfParams)
        && authKey is { Length: KeyBytes }
        && wrappedKeyPassword is { Length: > 0 and <= MaxWrappedKeyBytes };

    private static async Task<IResult> ConfirmAsync(
        HttpRequest request, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.ConfirmRequest, cancellationToken);
        if (string.IsNullOrWhiteSpace(body?.Token) || body.Token.Length > 256 || body.AuthKey is not { Length: KeyBytes })
        {
            return InvalidRequest();
        }

        // Código desconhecido, vencido ou de outra auth_key dão a mesma resposta: o código só confirma a conta de quem cadastrou.
        var confirmed = await store.ConfirmAsync(SecretHash.Of(body.Token), SecretHash.Of(body.AuthKey), time.GetUtcNow(), cancellationToken);
        return confirmed ? Results.NoContent() : Error("invalid_token", StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> LoginAsync(
        HttpRequest request, AccountStore store, AuthRateLimits limits, TimeProvider time, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.LoginRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        var deviceName = AccountRules.NormalizeDeviceName(body?.DeviceName);
        if (email is null || body?.AuthKey is not { Length: KeyBytes } || deviceName is null)
        {
            return InvalidRequest();
        }

        if (limits.HitEmail(email) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var account = await store.FindForLoginAsync(email, cancellationToken);
        var hash = SecretHash.Of(body.AuthKey);

        // E-mail desconhecido e chave errada dão a mesma resposta, depois de um caminho parecido.
        if (account is null)
        {
            _ = SecretHash.Equal(hash, DummyHash);
            return Error("unauthorized", StatusCodes.Status401Unauthorized);
        }

        if (!SecretHash.Equal(hash, account.AuthHash))
        {
            return Error("unauthorized", StatusCodes.Status401Unauthorized);
        }

        var token = SecretHash.NewToken();
        var deviceId = await store.CreateDeviceAsync(account.Id, account.AuthHash, deviceName, SecretHash.Of(token), time.GetUtcNow(), cancellationToken);
        if (deviceId is null)
        {
            // A conta mudou de senha ou sumiu entre a conferência e a criação.
            return Error("unauthorized", StatusCodes.Status401Unauthorized);
        }

        return Results.Json(new LoginResponse(token, deviceId.Value, account.WrappedKeyPassword), SyncJsonContext.Default.LoginResponse);
    }

    private static async Task<IResult> LogoutAsync(
        System.Security.Claims.ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        await store.RevokeDeviceAsync(user.AccountId(), user.DeviceId(), time.GetUtcNow(), cancellationToken);
        return Results.NoContent();
    }

    internal static IResult Unauthorized() => Error("unauthorized", StatusCodes.Status401Unauthorized);

    private static async Task<IResult> ChangePasswordAsync(
        HttpRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        AccountStore store,
        AuthRateLimits limits,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.ChangePasswordRequest, cancellationToken);
        if (body?.CurrentAuthKey is not { Length: KeyBytes }
            || !IsValidKeyMaterial(body.KdfSalt, body.KdfParams, body.AuthKey, body.WrappedKeyPassword))
        {
            return InvalidRequest();
        }

        var accountId = user.AccountId();

        // Reserva a tentativa antes de comparar, atomicamente: um token roubado não pode testar senhas sem o limite que o login tem.
        if (limits.ReserveKeyAttempt(accountId) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var currentHash = await store.FindAuthHashAsync(accountId, cancellationToken);
        if (currentHash is null || !SecretHash.Equal(SecretHash.Of(body.CurrentAuthKey), currentHash))
        {
            return Unauthorized();
        }

        limits.RefundKeyAttempt(accountId);

        var changed = await store.SetPasswordAsync(
            accountId,
            currentHash,
            new NewPassword(SecretHash.Of(body.AuthKey!), body.KdfSalt!, body.KdfParams!, body.WrappedKeyPassword!),
            user.DeviceId(),
            time.GetUtcNow(),
            cancellationToken);
        return changed ? Results.NoContent() : Unauthorized();
    }

    /// <summary>Acha a conta confirmada do e-mail e confere o código; e-mail desconhecido, conta não confirmada e código errado dão o mesmo resultado.</summary>
    private static async Task<RecoveryAccount?> CheckRecoveryCodeAsync(
        AccountStore store, string email, byte[] recoveryKey, CancellationToken cancellationToken)
    {
        var account = await store.FindForRecoveryAsync(email, cancellationToken);
        var hash = SecretHash.Of(recoveryKey);
        if (account is null)
        {
            _ = SecretHash.Equal(hash, DummyHash);
            return null;
        }

        return SecretHash.Equal(hash, account.RecoveryHash) ? account : null;
    }

    private static async Task<IResult> RecoverBeginAsync(
        HttpRequest request, AccountStore store, AuthRateLimits limits, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.RecoverBeginRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        if (email is null || body?.RecoveryKey is not { Length: KeyBytes })
        {
            return InvalidRequest();
        }

        if (limits.HitEmail(email) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var account = await CheckRecoveryCodeAsync(store, email, body.RecoveryKey, cancellationToken);
        return account is null
            ? Unauthorized()
            : Results.Json(new RecoverBeginResponse(account.WrappedKeyRecovery), SyncJsonContext.Default.RecoverBeginResponse);
    }

    private static async Task<IResult> RecoverAsync(
        HttpRequest request, AccountStore store, AuthRateLimits limits, TimeProvider time, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.RecoverRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        if (email is null || body?.RecoveryKey is not { Length: KeyBytes }
            || !IsValidKeyMaterial(body.KdfSalt, body.KdfParams, body.AuthKey, body.WrappedKeyPassword)
            || body.NewRecoveryKey is not { Length: KeyBytes }
            || body.NewWrappedKeyRecovery is not { Length: > 0 and <= MaxWrappedKeyBytes }
            // Trocar o código pelo mesmo deixaria o código usado valendo.
            || CryptographicOperations.FixedTimeEquals(body.NewRecoveryKey, body.RecoveryKey))
        {
            return InvalidRequest();
        }

        if (limits.HitEmail(email) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        // Confere o código aqui mesmo: nunca confia em que o begin tenha rodado antes.
        var account = await CheckRecoveryCodeAsync(store, email, body.RecoveryKey, cancellationToken);
        if (account is null)
        {
            return Unauthorized();
        }

        // O código só vale uma vez: se outro pedido com o mesmo código chegou antes, este não muda nada.
        var recovered = await store.RecoverAsync(
            account.Id,
            account.RecoveryHash,
            new NewPassword(SecretHash.Of(body.AuthKey!), body.KdfSalt!, body.KdfParams!, body.WrappedKeyPassword!),
            new NewRecovery(SecretHash.Of(body.NewRecoveryKey), body.NewWrappedKeyRecovery),
            time.GetUtcNow(),
            cancellationToken);
        return recovered ? Results.NoContent() : Unauthorized();
    }

    private static async Task<IResult> ListDevicesAsync(
        System.Security.Claims.ClaimsPrincipal user, AccountStore store, CancellationToken cancellationToken)
    {
        var currentId = user.DeviceId();
        var devices = await store.ListDevicesAsync(user.AccountId(), cancellationToken);
        var dtos = devices.Select(d => new DeviceDto(d.Id, d.Name, d.LastSeenAt, d.Id == currentId)).ToArray();
        return Results.Json(dtos, SyncJsonContext.Default.DeviceDtoArray);
    }

    private static async Task<IResult> RevokeDeviceAsync(
        Guid id, System.Security.Claims.ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var found = await store.RevokeOwnedDeviceAsync(user.AccountId(), id, time.GetUtcNow(), cancellationToken);
        return found ? Results.NoContent() : Error("not_found", StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> DeleteAccountAsync(
        HttpRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        AccountStore store,
        AuthRateLimits limits,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.DeleteAccountRequest, cancellationToken);
        if (body?.AuthKey is not { Length: KeyBytes })
        {
            return InvalidRequest();
        }

        var accountId = user.AccountId();
        if (limits.ReserveKeyAttempt(accountId) is { } retry)
        {
            return new RateLimitedResult(retry);
        }

        var currentHash = await store.FindAuthHashAsync(accountId, cancellationToken);
        if (currentHash is null || !SecretHash.Equal(SecretHash.Of(body.AuthKey), currentHash))
        {
            return Unauthorized();
        }

        limits.RefundKeyAttempt(accountId);

        await store.DeleteAccountAsync(accountId, cancellationToken);
        return Results.NoContent();
    }
}
