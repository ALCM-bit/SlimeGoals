using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória de <see cref="ITimerRepository"/>: no máximo um cronômetro.</summary>
public sealed class InMemoryTimerRepository : ITimerRepository
{
    private ActiveTimer? _timer;

    /// <summary>Apaga o cronômetro, se for da meta (a exclusão da meta).</summary>
    public void RemoveOf(Guid goalId)
    {
        if (_timer?.GoalId == goalId) _timer = null;
    }

    public Task<ActiveTimer?> GetAsync(CancellationToken ct = default) => Task.FromResult(_timer);

    public Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default)
    {
        if (_timer is not null) return Task.FromResult(false);
        _timer = timer;
        return Task.FromResult(true);
    }

    public Task SaveAsync(ActiveTimer timer, CancellationToken ct = default)
    {
        _timer = timer;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _timer = null;
        return Task.CompletedTask;
    }
}
