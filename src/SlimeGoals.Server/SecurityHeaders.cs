using Microsoft.Net.Http.Headers;

namespace SlimeGoals.Server;

/// <summary>
/// Cabeçalhos de toda resposta (ASVS V14.4, API8): <c>Cache-Control: no-store</c>, porque o token do aparelho e as chaves
/// embrulhadas não podem ficar em cache de proxy nem em disco, e <c>X-Content-Type-Options: nosniff</c>. Gravados no
/// início da resposta: valem também para a resposta de erro, que limpa os cabeçalhos do pedido.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var response = context.Response;
        response.OnStarting(() =>
        {
            response.Headers.CacheControl = "no-store";
            response.Headers.Remove(HeaderNames.Pragma);
            response.Headers.Remove(HeaderNames.Expires);
            response.Headers.XContentTypeOptions = "nosniff";
            return Task.CompletedTask;
        });
        return next(context);
    });
}
