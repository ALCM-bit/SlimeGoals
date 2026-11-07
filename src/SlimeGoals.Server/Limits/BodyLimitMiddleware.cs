using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Limits;

/// <summary>
/// Limita o corpo dos pedidos: <c>MaxBodyBytes</c> em <c>/sync/push</c> e 64 KiB nas demais rotas. Acima disso responde
/// <c>413 too_large</c>, tenha o pedido <c>Content-Length</c> ou venha em pedaços.
/// </summary>
public static class BodyLimitMiddleware
{
    /// <summary>Corpo máximo das rotas fora de <c>/sync/push</c>.</summary>
    public const long SmallBodyBytes = 64 * 1024;

    public static IApplicationBuilder UseBodyLimits(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        var max = request.Path.StartsWithSegments("/sync/push")
            ? context.RequestServices.GetRequiredService<IOptions<ServerOptions>>().Value.Limits.MaxBodyBytes
            : SmallBodyBytes;

        // O Kestrel recusa sozinho quando a característica aceita o limite; o resto vale também sem ele.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = max;
        }

        if (request.ContentLength > max)
        {
            await LimitResults.TooLarge().ExecuteAsync(context);
            return;
        }

        request.Body = new LimitedStream(request.Body, max);
        try
        {
            await next(context);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !context.Response.HasStarted)
        {
            await LimitResults.TooLarge().ExecuteAsync(context);
        }
    });

    /// <summary>Lança <c>BadHttpRequestException</c> 413 quando passam mais bytes que o máximo.</summary>
    private sealed class LimitedStream(Stream inner, long max) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _read += read;
            return _read > max
                ? throw new BadHttpRequestException("Corpo acima do máximo.", StatusCodes.Status413PayloadTooLarge)
                : read;
        }
    }
}
