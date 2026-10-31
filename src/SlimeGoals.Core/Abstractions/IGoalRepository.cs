using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para persistência de metas (implementada na Infrastructure).</summary>
public interface IGoalRepository
{
    Task AddAsync(Goal goal, CancellationToken ct = default);

    /// <summary>Substitui a meta. Lança <see cref="GoalNotFoundException"/> se o id não existe.</summary>
    Task UpdateAsync(Goal goal, CancellationToken ct = default);

    /// <summary>Null se não existe ou se foi excluída (DeletedAt preenchido).</summary>
    Task<Goal?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Nunca inclui excluídas; arquivadas só com includeArchived. Ordem: CreatedAt.</summary>
    Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default);
}
