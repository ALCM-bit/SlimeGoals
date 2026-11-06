namespace SlimeGoals.Core.Sync;

public interface ISyncTransport
{
    /// <summary>
    /// Baixa os registros com <c>seq</c> acima de <paramref name="after"/>. Com <paramref name="full"/>, o servidor nunca
    /// lança <see cref="SyncResetRequiredException"/>: quem lê tudo passa pela limpeza com registros vivos abaixo dela.
    /// Sem ele, lança sempre que <paramref name="after"/> estiver abaixo da limpeza, inclusive 0: o aparelho não sabe distinguir
    /// um servidor vazio de um que limpou o que ele conhece. Servidor sem limpeza nunca lança.
    /// </summary>
    Task<SyncPullPage> PullAsync(long after, int limit, bool full, CancellationToken ct = default);
    Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default);
}
