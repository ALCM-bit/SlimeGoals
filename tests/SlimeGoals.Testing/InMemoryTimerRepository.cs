using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória de <see cref="ITimerRepository"/>: no máximo um cronômetro.</summary>
public sealed class InMemoryTimerRepository : ITimerRepository
{
    private ActiveTimer? _timer;

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
