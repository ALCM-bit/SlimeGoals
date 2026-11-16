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

    /// <summary>Notas distintas dos registros não excluídos da meta, da mais recente para a mais antiga, até <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<string>> RecentNotesAsync(Guid goalId, int limit, CancellationToken ct = default);

    /// <summary>O registro não excluído com esse id, ou null.</summary>
    Task<Checkin?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Apaga o registro e leva a exclusão à sincronização. false se ele já não existia (nada muda).</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Apaga <paramref name="id"/> e grava <paramref name="replacement"/> numa transação só. false se o registro já não
    /// existia, e aí nada é gravado.
    /// </summary>
    Task<bool> ReplaceAsync(Guid id, Checkin replacement, CancellationToken ct = default);
}
