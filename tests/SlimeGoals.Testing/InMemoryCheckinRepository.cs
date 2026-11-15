using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória que respeita o contrato de <see cref="ICheckinRepository"/>.</summary>
public sealed class InMemoryCheckinRepository : ICheckinRepository
{
    private readonly List<Checkin> _checkins = [];

    /// <summary>Todos os registros gravados, inclusive excluídos, na ordem de gravação.</summary>
    public IReadOnlyList<Checkin> All => _checkins;

    /// <summary>Ids de registros que deixaram de contar (compensações substituídas).</summary>
    public HashSet<Guid> Superseded { get; } = [];

    /// <summary>Quando definido, toda escrita lança esta exceção.</summary>
    public Exception? FailWritesWith { get; set; }

    public Task AddAsync(Checkin checkin, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        _checkins.Add(checkin);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Checkin>>(Live(fromUtc, toUtc).OrderBy(c => c.DoneAt).ToList());

    public Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        Task.FromResult(Live(fromUtc, toUtc).Where(c => c.GoalId == goalId).Sum(c => c.Amount));

    public Task<bool> HasAnyAsync(Guid goalId, CancellationToken ct = default) =>
        Task.FromResult(_checkins.Any(c => c.GoalId == goalId && c.DeletedAt is null));

    public Task<IReadOnlyList<Guid>> CompensationIdsAsync(Guid goalId, DateOnly period, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(_checkins
            .Where(c => c.GoalId == goalId && c.DeletedAt is null && c.Compensation?.Period == period)
            .Select(c => c.Id).ToList());

    /// <summary>Apaga os registros da meta (a exclusão da meta).</summary>
    public Task<IReadOnlyList<string>> RecentNotesAsync(Guid goalId, int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_checkins
            .Where(c => c.GoalId == goalId && c.DeletedAt is null && c.Note is not null)
            .GroupBy(c => c.Note!)
            .OrderByDescending(g => g.Max(c => c.DoneAt))
            .Select(g => g.Key).Take(limit).ToList());

    public Task<Checkin?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_checkins.FirstOrDefault(c => c.Id == id && c.DeletedAt is null));

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        return Task.FromResult(_checkins.RemoveAll(c => c.Id == id) > 0);
    }

    public Task<bool> ReplaceAsync(Guid id, Checkin replacement, CancellationToken ct = default)
    {
        if (FailWritesWith is not null) throw FailWritesWith;
        if (_checkins.RemoveAll(c => c.Id == id) == 0)
            return Task.FromResult(false);
        _checkins.Add(replacement);
        return Task.FromResult(true);
    }

    public void RemoveOf(Guid goalId) => _checkins.RemoveAll(c => c.GoalId == goalId);

    private IEnumerable<Checkin> Live(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        _checkins.Where(c => c.DeletedAt is null && !Superseded.Contains(c.Id) && c.DoneAt >= fromUtc && c.DoneAt < toUtc);
}
