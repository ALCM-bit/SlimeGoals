using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>
/// Sessão de Pomodoro em memória. Imita a guarda da revisão e a de "sessão ou cronômetro" (com
/// <paramref name="timers"/>); os registros vão para <paramref name="checkins"/>.
/// </summary>
public sealed class InMemoryFocusSessionRepository(InMemoryCheckinRepository checkins, ITimerRepository? timers = null)
    : IFocusSessionRepository
{
    private readonly Lock _lock = new();
    private readonly List<FocusChunk> _chunks = [];
    private FocusSession? _session;

    /// <summary>Chamado no começo de <see cref="UpdateAsync"/>, antes da guarda: simula outro processo no meio.</summary>
    public Action? BeforeUpdate { get; set; }

    /// <summary>Simula outro processo gravando a sessão: só a revisão muda.</summary>
    public void ChangeElsewhere()
    {
        lock (_lock)
            if (_session is not null)
                _session = _session with { Revision = _session.Revision + 1 };
    }

    public Task<FocusSession?> GetAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult(_session);
    }

    public Task<IReadOnlyList<FocusChunk>> ChunksAsync(CancellationToken ct = default)
    {
        lock (_lock)
            return Task.FromResult<IReadOnlyList<FocusChunk>>(_chunks.OrderBy(c => c.EndedAt).ToList());
    }

    public async Task<bool> TryStartAsync(FocusSession session, CancellationToken ct = default)
    {
        if (timers is not null && await timers.GetAsync(ct) is not null)
            return false;
        lock (_lock)
        {
            if (_session is not null) return false;
            _session = session;
            _chunks.Clear();
            return true;
        }
    }

    public Task<bool> UpdateAsync(int expectedRevision, FocusSession? next, FocusChunk? chunk, Checkin? checkin,
        CancellationToken ct = default)
    {
        BeforeUpdate?.Invoke();
        lock (_lock)
        {
            if (_session is null || _session.Revision != expectedRevision)
                return Task.FromResult(false);
            // O repositório de registros em memória grava na hora: a falha dele sai antes de mudar a sessão.
            if (checkin is not null)
                checkins.AddAsync(checkin, ct).GetAwaiter().GetResult();
            if (next is null)
            {
                _session = null;
                _chunks.Clear();
            }
            else
            {
                _session = next with { Revision = expectedRevision + 1 };
                if (chunk is not null) _chunks.Add(chunk);
            }
            return Task.FromResult(true);
        }
    }

    public Task<bool> AssignChunkAsync(Guid chunkId, Checkin checkin, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _chunks.FindIndex(c => c.Id == chunkId);
            if (index < 0 || _chunks[index].CheckinId is not null)
                return Task.FromResult(false);
            checkins.AddAsync(checkin, ct).GetAwaiter().GetResult();
            _chunks[index] = _chunks[index] with { CheckinId = checkin.Id };
            return Task.FromResult(true);
        }
    }

    public Task<bool> MarkAlertedAsync(int step, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_session is null || _session.Step != step)
                return Task.FromResult(false);
            _session = _session with { AlertedStep = step };
            return Task.FromResult(true);
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _session = null;
            _chunks.Clear();
        }
        return Task.CompletedTask;
    }
}
