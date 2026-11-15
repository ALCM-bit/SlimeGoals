using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória de <see cref="ITimerRepository"/>: no máximo um cronômetro.</summary>
/// <summary>
/// Cronômetro em memória. Com <paramref name="checkins"/>, <see cref="SwitchAsync"/> grava o trecho nele;
/// <see cref="FailSwitches"/> simula o cronômetro mudado por outro processo.
/// </summary>
public sealed class InMemoryTimerRepository(InMemoryCheckinRepository? checkins = null) : ITimerRepository
{
    private ActiveTimer? _timer;

    public bool FailSwitches { get; set; }

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

    public Task<bool> StopAsync(DateTimeOffset at, CancellationToken ct = default)
    {
        if (_timer is not { StoppedAt: null } running) return Task.FromResult(false);
        _timer = running with { StoppedAt = at };
        return Task.FromResult(true);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        _timer = null;
        return Task.CompletedTask;
    }

    /// <summary>Quantas vezes o nome da tarefa foi gravado.</summary>
    public int SetTaskCalls { get; private set; }

    public Task<bool> SetTaskAsync(string? task, CancellationToken ct = default)
    {
        SetTaskCalls++;
        if (_timer is null) return Task.FromResult(false);
        _timer = _timer with { Task = task };
        return Task.FromResult(true);
    }

    public async Task<bool> SwitchAsync(DateTimeOffset expectedStartedAt, ActiveTimer next, Checkin? chunk, CancellationToken ct = default)
    {
        if (checkins is null && chunk is not null)
            throw new InvalidOperationException("Passe o repositório de registros para trocar de tarefa.");
        if (FailSwitches || _timer is not { StoppedAt: null } current || current.GoalId != next.GoalId
            || current.StartedAt != expectedStartedAt)
            return false;
        if (chunk is not null)
            await checkins!.AddAsync(chunk, ct);
        _timer = next;
        return true;
    }
}
