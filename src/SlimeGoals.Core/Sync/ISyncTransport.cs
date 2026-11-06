namespace SlimeGoals.Core.Sync;

public interface ISyncTransport
{
    /// <summary>
    /// Baixa os registros com <c>seq</c> acima de <paramref name="after"/>. Com <paramref name="full"/>, o servidor nunca
    /// lança <see cref="SyncResetRequiredException"/>: quem lê tudo passa pela limpeza com registros vivos abaixo dela.
    /// </summary>
    Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default);
    Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default);
}
