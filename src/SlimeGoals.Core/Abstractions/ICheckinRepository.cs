using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para os registros de progresso (implementada na Infrastructure).</summary>
public interface ICheckinRepository
{
    Task AddAsync(Checkin checkin, CancellationToken ct = default);

    /// <summary>Registros com DoneAt em [fromUtc, toUtc), sem excluídos, ordenados por DoneAt.</summary>
    Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Soma dos valores da meta com DoneAt em [fromUtc, toUtc), sem excluídos.</summary>
    Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>A meta tem algum registro não excluído.</summary>
    Task<bool> HasAnyAsync(Guid goalId, CancellationToken ct = default);
}
