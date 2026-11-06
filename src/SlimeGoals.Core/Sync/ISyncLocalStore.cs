namespace SlimeGoals.Core.Sync;

public interface ISyncLocalStore
{
    Task<IReadOnlyList<OutboxItem>> ReadOutboxAsync(int limit, CancellationToken ct = default);
    Task ConfirmPushAsync(IReadOnlyList<PushConfirmation> confirmations, CancellationToken ct = default);
    /// <summary>Aplica a página e avança last_seq numa transação. Devolve as metas excluídas.</summary>
    Task<IReadOnlyList<Guid>> ApplyPageAsync(IReadOnlyList<IncomingRecord> records, long lastSeq, Guid deviceId, CancellationToken ct = default);
    Task BeginFullResyncAsync(CancellationToken ct = default);
    /// <summary>Sem efeito fora de uma sincronização completa. Devolve as metas excluídas.</summary>
    Task<IReadOnlyList<Guid>> FinishFullResyncAsync(CancellationToken ct = default);
    Task RecordCycleAsync(DateTimeOffset? syncedAt, string? error, CancellationToken ct = default);
    Task<SyncStatus> GetStatusAsync(CancellationToken ct = default);
}
