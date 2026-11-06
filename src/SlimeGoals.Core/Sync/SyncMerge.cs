namespace SlimeGoals.Core.Sync;

/// <summary>Regra de conflito entre edições da mesma meta (spec de sincronização §4).</summary>
public static class SyncMerge
{
    /// <summary>
    /// A edição mais nova vence; em empate, o maior id de aparelho (texto "D", ordinal).
    /// Mesma data e mesmo aparelho é a mesma versão (aceite perdido): não é vitória local.
    /// </summary>
    public static bool LocalWins(DateTimeOffset localUpdatedAt, Guid localDevice, DateTimeOffset remoteUpdatedAt, Guid remoteDevice)
    {
        if (localUpdatedAt != remoteUpdatedAt) return localUpdatedAt > remoteUpdatedAt;
        return string.CompareOrdinal(localDevice.ToString("D"), remoteDevice.ToString("D")) > 0;
    }
}
