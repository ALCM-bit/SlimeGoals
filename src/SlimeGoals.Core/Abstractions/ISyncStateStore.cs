namespace SlimeGoals.Core.Abstractions;

/// <summary>Estado local da sincronização: ligada ou não, identidade do dispositivo e último número visto do servidor.</summary>
public sealed record SyncState(bool Enabled, Guid? DeviceId, long LastSeq);

/// <summary>Porta de saída para ligar e desligar a sincronização.</summary>
public interface ISyncStateStore
{
    Task<SyncState> GetAsync(CancellationToken ct = default);

    /// <summary>Liga e põe na fila tudo o que existe. Repetir não muda nada.</summary>
    Task EnableAsync(CancellationToken ct = default);

    /// <summary>Desliga e limpa fila e estado (RS07). Metas e registros ficam.</summary>
    Task DisableAsync(CancellationToken ct = default);
}
