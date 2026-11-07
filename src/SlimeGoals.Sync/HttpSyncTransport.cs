using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Sync;

/// <summary>
/// Fala com o servidor de sincronização. <c>410</c> vira <see cref="SyncResetRequiredException"/>; qualquer outra falha,
/// inclusive uma resposta que não dá para ler, vira <see cref="HttpRequestException"/> com <c>StatusCode</c>.
/// </summary>
/// <param name="http">Cliente com <c>BaseAddress</c> do servidor.</param>
/// <param name="token">Token do aparelho a cada pedido; nulo envia sem <c>Authorization</c>.</param>
public sealed class HttpSyncTransport(HttpClient http, Func<CancellationToken, ValueTask<string?>> token) : ISyncTransport
{
    public async Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default)
    {
        var uri = string.Create(CultureInfo.InvariantCulture, $"sync/pull?after={after}&limit={limit}&full={(full ? 1 : 0)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var page = await SendAsync(request, SyncJsonContext.Default.PullResponse, IsComplete, ct);
        return new SyncPullPage(page.Records.Select(r => new RemoteRecord(r.Key, r.Seq, r.Blob)).ToList(), page.HasMore);
    }

    public async Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default)
    {
        var dtos = items.Select(i => new PushItemDto(i.RecordKey, i.BaseSeq, i.Blob, i.Blob is null)).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, "sync/push")
        {
            Content = JsonContent.Create(new PushRequest(dtos), SyncJsonContext.Default.PushRequest),
        };
        var result = await SendAsync(request, SyncJsonContext.Default.PushResponse, IsComplete, ct);
        return new PushResult(result.Accepted.Select(a => new PushAccepted(a.Key, a.Seq)).ToList(), result.Conflicts);
    }

    // Campo ausente o JSON já recusa; estes pegam o null explícito, que ele deixa passar.
    private static bool IsComplete(PullResponse page) =>
        page.Records is not null && page.Records.All(r => r?.Key is not null);

    private static bool IsComplete(PushResponse result) =>
        result.Accepted is not null && result.Accepted.All(a => a?.Key is not null)
        && result.Conflicts is not null && result.Conflicts.All(key => key is not null);

    private async Task<T> SendAsync<T>(
        HttpRequestMessage request, JsonTypeInfo<T> typeInfo, Func<T, bool> isComplete, CancellationToken ct)
        where T : class
    {
        if (await token(ct) is { } value)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        }

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            throw new SyncResetRequiredException();
        }

        response.EnsureSuccessStatusCode();
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var body = await JsonSerializer.DeserializeAsync(stream, typeInfo, ct);
            return body is not null && isComplete(body) ? body : throw Unreadable(response.StatusCode, null);
        }
        catch (JsonException e)
        {
            throw Unreadable(response.StatusCode, e);
        }
    }

    private static HttpRequestException Unreadable(HttpStatusCode status, Exception? inner) =>
        new(HttpRequestError.InvalidResponse, "Resposta do servidor de sincronização ilegível.", inner, status);
}
