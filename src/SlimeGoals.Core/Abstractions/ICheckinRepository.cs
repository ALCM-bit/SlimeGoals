using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para os registros de progresso (implementada na Infrastructure).</summary>
public interface ICheckinRepository
{
    Task AddAsync(Checkin checkin, CancellationToken ct = default);

    /// <summary>Registros com DoneAt em [fromUtc, toUtc), sem excluídos nem substituídos, ordenados por DoneAt.</summary>
    Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>Soma dos valores da meta com DoneAt em [fromUtc, toUtc), sem excluídos nem substituídos.</summary>
    Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    /// <summary>A meta tem algum registro não excluído.</summary>
    Task<bool> HasAnyAsync(Guid goalId, CancellationToken ct = default);

    /// <summary>Ids de todas as compensações da meta no período que o aparelho tem, inclusive as que deixaram de contar.</summary>
    Task<IReadOnlyList<Guid>> CompensationIdsAsync(Guid goalId, DateOnly period, CancellationToken ct = default);
}
