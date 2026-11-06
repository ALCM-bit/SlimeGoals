using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>
/// Implementação em memória que respeita o contrato de <see cref="IGoalRepository"/>. Os repositórios recebidos
/// são os que <see cref="DeleteAsync"/> limpa junto, como a transação do banco.
/// </summary>
public sealed class InMemoryGoalRepository(InMemoryCheckinRepository? checkins = null, InMemoryReminderLogRepository? log = null,
    InMemoryTimerRepository? timers = null) : IGoalRepository
{
    private readonly Dictionary<Guid, Goal> _goals = new();

    public int Count => _goals.Count;

    /// <summary>Quando definido, toda escrita lança esta exceção (simula banco travado, disco cheio...).</summary>
    public Exception? FailWritesWith { get; set; }

    /// <summary>Quando definido, toda leitura lança esta exceção.</summary>
    public Exception? FailReadsWith { get; set; }

    public Task AddAsync(Goal goal, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        _goals.Add(goal.Id, goal);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Goal goal, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        if (!_goals.ContainsKey(goal.Id)) throw new GoalNotFoundException(goal.Id);
        _goals[goal.Id] = goal;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        if (!_goals.Remove(id)) throw new GoalNotFoundException(id);
        checkins?.RemoveOf(id);
        log?.RemoveOf(id);
        timers?.RemoveOf(id);
        return Task.CompletedTask;
    }

    public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) =>
        FailReadsWith is not null ? throw FailReadsWith : Task.FromResult(_goals.TryGetValue(id, out var g) && g.DeletedAt is null ? g : null);

    public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
        FailReadsWith is not null ? throw FailReadsWith : Task.FromResult<IReadOnlyList<Goal>>(_goals.Values
            .Where(g => g.DeletedAt is null && (includeArchived || g.ArchivedAt is null))
            .OrderBy(g => g.CreatedAt).ToList());
}
