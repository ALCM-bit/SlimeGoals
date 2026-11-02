using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para o log de lembretes disparados (implementada na Infrastructure).</summary>
public interface IReminderLogRepository
{
    Task AddAsync(ReminderLogEntry entry, CancellationToken ct = default);

    Task<ReminderLogEntry?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Grava a resposta só se ainda não há resposta; devolve false se já havia (ou o id não existe).</summary>
    Task<bool> RespondAsync(Guid id, ReminderResponse response, DateTimeOffset respondedAt, CancellationToken ct = default);

    /// <summary>Disparos com FiredAt em [fromUtc, toUtc), ordenados por FiredAt.</summary>
    Task<IReadOnlyList<ReminderLogEntry>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Disparos sem resposta, ordenados por FiredAt.</summary>
    Task<IReadOnlyList<ReminderLogEntry>> ListPendingAsync(CancellationToken ct = default);
}
