using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Fakes;

/// <summary>Implementação em memória que respeita o contrato de <see cref="IGoalRepository"/>.</summary>
internal sealed class InMemoryGoalRepository : IGoalRepository
{
    private readonly Dictionary<Guid, Goal> _goals = new();

    public int Count => _goals.Count;

    public Task AddAsync(Goal goal, CancellationToken ct = default)
    {
        _goals.Add(goal.Id, goal);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Goal goal, CancellationToken ct = default)
    {
        if (!_goals.ContainsKey(goal.Id)) throw new GoalNotFoundException(goal.Id);
        _goals[goal.Id] = goal;
        return Task.CompletedTask;
    }

    public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_goals.TryGetValue(id, out var g) && g.DeletedAt is null ? g : null);

    public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Goal>>(_goals.Values
            .Where(g => g.DeletedAt is null && (includeArchived || g.ArchivedAt is null))
            .OrderBy(g => g.CreatedAt).ToList());
}
