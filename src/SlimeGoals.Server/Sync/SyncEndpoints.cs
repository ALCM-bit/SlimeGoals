using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Server.Auth;
using SlimeGoals.Server.Limits;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Sync;

/// <summary>Rotas de envio e leitura de registros: <c>/sync/*</c>.</summary>
public static class SyncEndpoints
{
    private const int MaxRecordKeyLength = 128;

    public static void MapSyncEndpoints(this IEndpointRouteBuilder routes)
    {
        var sync = routes.MapGroup("/sync").RequireAuthorization();
        sync.MapGet("/pull", PullAsync);
        sync.MapPost("/push", PushAsync);
    }

    private static async Task<IResult> PullAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        RecordStore store,
        IOptions<ServerOptions> options,
        CancellationToken cancellationToken)
    {
        var sync = options.Value.Sync;
        if (!TryReadNumber(request.Query, "after", 0, out var after)
            || !TryReadNumber(request.Query, "limit", sync.DefaultLimit, out var limit) || limit < 1
            || !TryReadFull(request.Query, out var full))
        {
            return AccountEndpoints.InvalidRequest();
        }

        // Acima do máximo reduz em vez de recusar: has_more continua certo, e o aparelho nem percebe (decisão 12).
        try
        {
            var page = await store.PullAsync(user.AccountId(), after, (int)Math.Min(limit, sync.MaxLimit), full, cancellationToken);
            return Results.Json(page, SyncJsonContext.Default.PullResponse);
        }
        catch (ResetRequiredException)
        {
            return AccountEndpoints.ResetRequired();
        }
    }

    private static async Task<IResult> PushAsync(
        HttpRequest request,
        ClaimsPrincipal user,
        RecordStore store,
        IOptions<ServerOptions> options,
        CancellationToken cancellationToken)
    {
        var limits = options.Value.Limits;
        var body = await AccountEndpoints.ReadBodyAsync(request, SyncJsonContext.Default.PushRequest, cancellationToken);
        if (body?.Items is not { } items || items.Length > limits.MaxBatchItems || !items.All(IsValid))
        {
            return AccountEndpoints.InvalidRequest();
        }

        if (items.Any(i => i.Blob?.Length > limits.MaxBlobBytes))
        {
            return LimitResults.TooLarge();
        }

        try
        {
            var result = await store.PushAsync(user.AccountId(), items, cancellationToken);
            return result is null
                ? AccountEndpoints.Unauthorized()
                : Results.Json(result, SyncJsonContext.Default.PushResponse);
        }
        catch (QuotaExceededException)
        {
            return LimitResults.QuotaExceeded();
        }
    }

    /// <summary>Número sem sinal; o valor ausente vale <paramref name="fallback"/>, e repetido é inválido.</summary>
    private static bool TryReadNumber(IQueryCollection query, string name, long fallback, out long value)
    {
        value = fallback;
        return !query.TryGetValue(name, out var raw)
            || (raw.Count == 1 && long.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out value));
    }

    private static bool TryReadFull(IQueryCollection query, out bool full)
    {
        full = false;
        if (!query.TryGetValue("full", out var raw))
        {
            return true;
        }

        full = raw == "1";
        return raw == "1" || raw == "0";
    }

    // Caractere de controle fica de fora também porque um NUL faria o PostgreSQL lançar exceção.
    private static bool IsValid(PushItemDto? item) =>
        item is { RecordKey: { Length: >= 1 and <= MaxRecordKeyLength } key, BaseSeq: >= 0 }
        && !AccountEndpoints.HasControl(key)
        && item.Blob is null == item.Delete;
}
