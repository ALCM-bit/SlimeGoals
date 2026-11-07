using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync.Account;

/// <summary>
/// Cliente das rotas de conta do servidor. Toda falha vira <see cref="AccountApiException"/>; o cancelamento do
/// chamador continua sendo <see cref="OperationCanceledException"/>.
/// </summary>
/// <param name="http">Cliente com <c>BaseAddress</c> do servidor.</param>
public sealed class HttpAccountApi(HttpClient http)
{
    public async Task<PreloginResponse> PreloginAsync(string email, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Post, "auth/prelogin", null, Json(new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest),
            SyncJsonContext.Default.PreloginResponse, r => r.KdfSalt is not null && r.KdfParams is not null, ct);

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "auth/register", null, Json(request, SyncJsonContext.Default.RegisterRequest), ct);

    public async Task ConfirmAsync(string token, byte[] authKey, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Post, "auth/confirm", null, Json(new ConfirmRequest(token, authKey), SyncJsonContext.Default.ConfirmRequest), ct);

    public async Task<LoginResponse> LoginAsync(string email, byte[] authKey, string deviceName, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Post, "auth/login", null, Json(new LoginRequest(email, authKey, deviceName), SyncJsonContext.Default.LoginRequest),
            SyncJsonContext.Default.LoginResponse, r => r.Token is not null && r.WrappedKeyPassword is not null, ct);

    public async Task LogoutAsync(string deviceToken, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "auth/logout", deviceToken, null, ct);

    public async Task ChangePasswordAsync(string deviceToken, ChangePasswordRequest request, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "auth/password", deviceToken, Json(request, SyncJsonContext.Default.ChangePasswordRequest), ct);

    public async Task<RecoverBeginResponse> RecoverBeginAsync(string email, byte[] recoveryKey, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Post, "auth/recover/begin", null,
            Json(new RecoverBeginRequest(email, recoveryKey), SyncJsonContext.Default.RecoverBeginRequest),
            SyncJsonContext.Default.RecoverBeginResponse, r => r.WrappedKeyRecovery is not null, ct);

    public async Task RecoverAsync(RecoverRequest request, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Post, "auth/recover", null, Json(request, SyncJsonContext.Default.RecoverRequest), ct);

    public async Task<IReadOnlyList<DeviceDto>> ListDevicesAsync(string deviceToken, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Get, "devices", deviceToken, null, SyncJsonContext.Default.DeviceDtoArray,
            list => list.All(d => d?.Name is not null), ct);

    public async Task RevokeDeviceAsync(string deviceToken, Guid id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, $"devices/{id}", deviceToken, null, ct);

    public async Task DeleteAccountAsync(string deviceToken, byte[] authKey, CancellationToken ct = default) =>
        await SendAsync(
            HttpMethod.Delete, "account", deviceToken, Json(new DeleteAccountRequest(authKey), SyncJsonContext.Default.DeleteAccountRequest), ct);

    private static HttpContent Json<T>(T value, JsonTypeInfo<T> typeInfo) => JsonContent.Create(value, typeInfo);

    private async Task SendAsync(HttpMethod method, string uri, string? deviceToken, HttpContent? content, CancellationToken ct) =>
        await ExecuteAsync<object>(method, uri, deviceToken, content, null, null, ct);

    private async Task<T> SendAsync<T>(
        HttpMethod method, string uri, string? deviceToken, HttpContent? content, JsonTypeInfo<T> typeInfo, Func<T, bool> isComplete, CancellationToken ct)
        where T : class =>
        (await ExecuteAsync(method, uri, deviceToken, content, typeInfo, isComplete, ct))!;

    private async Task<T?> ExecuteAsync<T>(
        HttpMethod method, string uri, string? deviceToken, HttpContent? content, JsonTypeInfo<T>? typeInfo, Func<T, bool>? isComplete, CancellationToken ct)
        where T : class
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        if (deviceToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        }

        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorOfAsync(response, ct);
            }

            if (typeInfo is null)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var body = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct);
            return body is not null && isComplete!(body) ? body : throw new AccountApiException(AccountErrorKind.Unavailable);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException
            || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new AccountApiException(AccountErrorKind.Unavailable, inner: e);
        }
    }

    private static async Task<AccountApiException> ErrorOfAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;
        if ((int)status >= 500 || (int)status < 400)
        {
            return new AccountApiException(AccountErrorKind.Unavailable);
        }

        return status switch
        {
            HttpStatusCode.Unauthorized => new AccountApiException(AccountErrorKind.Unauthorized),
            HttpStatusCode.NotFound => new AccountApiException(AccountErrorKind.NotFound),
            HttpStatusCode.RequestEntityTooLarge => new AccountApiException(AccountErrorKind.TooLarge),
            HttpStatusCode.TooManyRequests => new AccountApiException(AccountErrorKind.RateLimited, RetryAfterOf(response)),
            HttpStatusCode.BadRequest when await ErrorCodeAsync(response, ct) == "invalid_token" =>
                new AccountApiException(AccountErrorKind.InvalidToken),
            _ => new AccountApiException(AccountErrorKind.InvalidRequest),
        };
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta)
        {
            return delta;
        }

        return retry?.Date is { } date ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks)) : null;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return (await JsonSerializer.DeserializeAsync(stream, SyncJsonContext.Default.ErrorResponse, ct))?.Error;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }
}
