using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using SlimeGoals.Server.Auth;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Accounts;

/// <summary>Rotas de cadastro, entrada e saída: <c>/auth/*</c>.</summary>
public static class AccountEndpoints
{
    private const int MaxEmailLength = 254;
    private const int MaxDeviceNameLength = 100;
    private const int KeyBytes = 32;
    private const int MaxKdfParamsLength = 256;
    private const int MaxWrappedKeyBytes = 256;

    // Hash sem dono: o login de e-mail desconhecido compara contra ele, para o caminho ser parecido.
    private static readonly byte[] DummyHash = SecretHash.Of(new byte[KeyBytes]);

    public static void MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var auth = routes.MapGroup("/auth");
        auth.MapPost("/prelogin", PreloginAsync);
        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/confirm", ConfirmAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/logout", LogoutAsync).RequireAuthorization();
    }

    /// <summary>Remove espaços e usa minúsculas; nulo se vazio, grande demais ou sem arroba (decisão 15).</summary>
    public static string? NormalizeEmail(string? email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) || normalized.Length > MaxEmailLength || !normalized.Contains('@')
            || normalized.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            ? null
            : normalized;
    }

    private static bool HasControl(string text) => text.Any(char.IsControl);

    private static IResult Error(string code, int status) =>
        Results.Json(new ErrorResponse(code), SyncJsonContext.Default.ErrorResponse, statusCode: status);

    private static IResult InvalidRequest() => Error("invalid_request", StatusCodes.Status400BadRequest);

    private static async Task<T?> ReadBodyAsync<T>(HttpRequest request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await request.ReadFromJsonAsync(typeInfo, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return null;
        }
    }

    private static async Task<IResult> PreloginAsync(
        HttpRequest request, AccountStore store, IOptions<ServerOptions> options, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.PreloginRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        if (email is null)
        {
            return InvalidRequest();
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

        var now = time.GetUtcNow();
        var token = SecretHash.NewToken();
        var tokenHash = SecretHash.Of(token);
        var created = await store.TryCreateAsync(
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

        // Mesma resposta para e-mail novo e já usado (decisão 6); só a conta nova manda e-mail.
        if (created)
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
        body.KdfSalt is { Length: >= 16 and <= 64 }
        && !string.IsNullOrWhiteSpace(body.KdfParams) && body.KdfParams.Length <= MaxKdfParamsLength
        && !HasControl(body.KdfParams)
        && body.AuthKey is { Length: KeyBytes }
        && body.RecoveryKey is { Length: KeyBytes }
        && body.WrappedKeyPassword is { Length: > 0 and <= MaxWrappedKeyBytes }
        && body.WrappedKeyRecovery is { Length: > 0 and <= MaxWrappedKeyBytes };

    private static async Task<IResult> ConfirmAsync(
        HttpRequest request, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.ConfirmRequest, cancellationToken);
        if (string.IsNullOrWhiteSpace(body?.Token) || body.Token.Length > 256)
        {
            return InvalidRequest();
        }

        var confirmed = await store.ConfirmAsync(SecretHash.Of(body.Token), time.GetUtcNow(), cancellationToken);
        return confirmed ? Results.NoContent() : Error("invalid_token", StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> LoginAsync(
        HttpRequest request, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(request, SyncJsonContext.Default.LoginRequest, cancellationToken);
        var email = NormalizeEmail(body?.Email);
        var deviceName = body?.DeviceName?.Trim();
        if (email is null || body?.AuthKey is not { Length: KeyBytes }
            || string.IsNullOrEmpty(deviceName) || deviceName.Length > MaxDeviceNameLength || HasControl(deviceName))
        {
            return InvalidRequest();
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
        var deviceId = await store.CreateDeviceAsync(account.Id, deviceName, SecretHash.Of(token), time.GetUtcNow(), cancellationToken);
        return Results.Json(new LoginResponse(token, deviceId, account.WrappedKeyPassword), SyncJsonContext.Default.LoginResponse);
    }

    private static async Task<IResult> LogoutAsync(
        System.Security.Claims.ClaimsPrincipal user, AccountStore store, TimeProvider time, CancellationToken cancellationToken)
    {
        await store.RevokeDeviceAsync(user.AccountId(), user.DeviceId(), time.GetUtcNow(), cancellationToken);
        return Results.NoContent();
    }
}
