using SlimeGoals.Server.Auth;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Limits;

/// <summary><c>429 rate_limited</c> com <c>Retry-After</c> em segundos inteiros, nunca abaixo de 1.</summary>
public sealed class RateLimitedResult(TimeSpan retryAfter) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(new ErrorResponse("rate_limited"), SyncJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status429TooManyRequests)
            .ExecuteAsync(httpContext);
    }
}

/// <summary>Respostas e filtros dos limites de tamanho, taxa e cota.</summary>
public static class LimitResults
{
    public static IResult TooLarge() =>
        Results.Json(new ErrorResponse("too_large"), SyncJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status413PayloadTooLarge);

    public static IResult QuotaExceeded() =>
        Results.Json(new ErrorResponse("quota_exceeded"), SyncJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Limite por IP no grupo <c>/auth</c>.</summary>
    public static RouteGroupBuilder WithIpRateLimit(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
        {
            var ip = ClientIp.Of(context.HttpContext);
            return context.HttpContext.RequestServices.GetRequiredService<RateLimits>().HitIp(ip) is { } retry
                ? new RateLimitedResult(retry)
                : await next(context);
        });

    /// <summary>Limite por aparelho no grupo <c>/sync</c>; vai depois da autorização, porque lê o aparelho do token.</summary>
    public static RouteGroupBuilder WithDeviceRateLimit(this RouteGroupBuilder group) =>
        group.AddEndpointFilter(async (context, next) =>
            context.HttpContext.RequestServices.GetRequiredService<RateLimits>().HitDevice(context.HttpContext.User.DeviceId()) is { } retry
                ? new RateLimitedResult(retry)
                : await next(context));
}
