using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória que respeita o contrato de <see cref="ICheckinRepository"/>.</summary>
public sealed class InMemoryCheckinRepository : ICheckinRepository
{
    private readonly List<Checkin> _checkins = [];

    /// <summary>Todos os registros gravados, inclusive excluídos, na ordem de gravação.</summary>
    public IReadOnlyList<Checkin> All => _checkins;

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

    /// <summary>Apaga os registros da meta (a exclusão da meta).</summary>
    public void RemoveOf(Guid goalId) => _checkins.RemoveAll(c => c.GoalId == goalId);

    private IEnumerable<Checkin> Live(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        _checkins.Where(c => c.DeletedAt is null && c.DoneAt >= fromUtc && c.DoneAt < toUtc);
}
