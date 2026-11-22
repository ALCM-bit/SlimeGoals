using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para as tags e suas ligações com as metas (implementada na Infrastructure).</summary>
public interface ITagRepository
{
    /// <summary>Todas as tags, inclusive as excluídas.</summary>
    Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default);

    /// <summary>Todas as ligações, inclusive as removidas.</summary>
    Task<IReadOnlyList<GoalTag>> ListLinksAsync(CancellationToken ct = default);

    /// <summary>Grava (upsert por id) as tags e ligações numa transação e as leva para a fila de sincronização.</summary>
    Task SaveAsync(TagChanges changes, CancellationToken ct = default);
}
