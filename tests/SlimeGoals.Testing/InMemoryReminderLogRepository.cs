using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória que respeita o contrato de <see cref="IReminderLogRepository"/>.</summary>
public sealed class InMemoryReminderLogRepository : IReminderLogRepository
{
    private readonly List<ReminderLogEntry> _entries = [];

    /// <summary>Todas as entradas, na ordem de gravação.</summary>
    public IReadOnlyList<ReminderLogEntry> All => _entries;

    /// <summary>Apaga os disparos da meta (a exclusão da meta).</summary>
    public void RemoveOf(Guid goalId) => _entries.RemoveAll(e => e.GoalId == goalId);

    public Task AddAsync(ReminderLogEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<ReminderLogEntry?> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(_entries.FirstOrDefault(e => e.Id == id));

    public Task<bool> RespondAsync(Guid id, ReminderResponse response, DateTimeOffset respondedAt, CancellationToken ct = default)
    {
        var index = _entries.FindIndex(e => e.Id == id);
        if (index < 0 || _entries[index].Response is not null)
            return Task.FromResult(false);
        _entries[index] = _entries[index] with { Response = response, RespondedAt = respondedAt };
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ReminderLogEntry>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReminderLogEntry>>(
            _entries.Where(e => e.FiredAt >= fromUtc && e.FiredAt < toUtc).OrderBy(e => e.FiredAt).ToList());

    public Task<IReadOnlyList<ReminderLogEntry>> ListPendingAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ReminderLogEntry>>(
            _entries.Where(e => e.Response is null).OrderBy(e => e.FiredAt).ToList());
}
