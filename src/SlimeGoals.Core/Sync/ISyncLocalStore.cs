namespace SlimeGoals.Core.Sync;

public interface ISyncLocalStore
{
    Task<IReadOnlyList<OutboxItem>> ReadOutboxAsync(int limit, CancellationToken ct = default);
    /// <summary>Itens da fila cuja chave no servidor ainda não foi guardada, em ordem de fila.</summary>
    Task<IReadOnlyList<Guid>> ReadUnkeyedAsync(int limit, CancellationToken ct = default);
    /// <summary>Chamar antes de cada envio: se o aceite se perder, a exclusão vinda de outro aparelho ainda acha o registro pela chave.</summary>
    Task RememberKeysAsync(IReadOnlyList<OutboxKey> keys, CancellationToken ct = default);
    Task ConfirmPushAsync(IReadOnlyList<PushConfirmation> confirmations, CancellationToken ct = default);
    /// <summary>Aplica a página e avança last_seq numa transação. Devolve as metas excluídas.</summary>
    Task<IReadOnlyList<Guid>> ApplyPageAsync(IReadOnlyList<IncomingRecord> records, long lastSeq, Guid deviceId, CancellationToken ct = default);
    Task BeginFullResyncAsync(CancellationToken ct = default);
    /// <summary>Sem efeito fora de uma sincronização completa. Devolve as metas excluídas.</summary>
    Task<IReadOnlyList<Guid>> FinishFullResyncAsync(CancellationToken ct = default);
    Task RecordCycleAsync(DateTimeOffset? syncedAt, string? error, CancellationToken ct = default);
    Task<SyncStatus> GetStatusAsync(CancellationToken ct = default);
}
