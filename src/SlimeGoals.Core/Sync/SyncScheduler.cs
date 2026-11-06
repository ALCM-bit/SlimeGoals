using System.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Core.Sync;

/// <summary>
/// Decide quando o ciclo de sincronização roda (spec de sincronização §4, "Quando sincroniza"): ao iniciar,
/// 5 s depois da última mudança, a cada 15 min e sob pedido. Falha espera 30 s, 60 s, 120 s... até 15 min.
/// Um temporizador só reprograma o próximo ciclo, e nunca há dois ciclos ao mesmo tempo.
/// </summary>
public sealed class SyncScheduler : IDisposable
{
    public static readonly TimeSpan ChangeDelay = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan Period = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(15);

    private readonly Func<CancellationToken, Task<SyncCycleResult>> _runCycle;
    private readonly DataChangeNotifier _changes;
    private readonly ITimer _timer;
    private readonly object _lock = new();

    private bool _started;
    private bool _cycleRunning;
    private TimeSpan? _rerunDelay; // pedido que chegou com um ciclo em andamento
    private int _failures;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public SyncScheduler(Func<CancellationToken, Task<SyncCycleResult>> runCycle, DataChangeNotifier changes, TimeProvider time)
    {
        _runCycle = runCycle;
        _changes = changes;
        _timer = time.CreateTimer(_ => _ = RunAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _changes.Changed += OnChanged;
    }

    /// <summary>Começa a agendar e roda um ciclo agora.</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _started = true;
        }
        RequestNow();
    }

    /// <summary>Cancela o ciclo em andamento (sem esperar), apaga o agendamento e passa a ignorar mudanças.</summary>
    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            _started = false;
            _rerunDelay = null;
            _failures = 0;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            cts = _cts;
        }
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // o ciclo acabou entre a leitura e o cancelamento
        }
    }

    /// <summary>Pede um ciclo imediato (por exemplo, ao voltar ao primeiro plano).</summary>
    public void RequestNow()
    {
        lock (_lock)
        {
            if (!_started)
                return;
            if (_cycleRunning)
            {
                _rerunDelay = TimeSpan.Zero;
                return;
            }
        }
        _ = RunAsync();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _changes.Changed -= OnChanged;
        Stop();
        _timer.Dispose();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            if (!_started)
                return;
            if (_cycleRunning)
            {
                _rerunDelay ??= ChangeDelay;
                return;
            }
            _timer.Change(ChangeDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            CancellationToken token;
            lock (_lock)
            {
                if (!_started || _cycleRunning)
                    return;
                _cycleRunning = true;
                _rerunDelay = null;
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _cts = new CancellationTokenSource();
                token = _cts.Token;
            }

            SyncCycleResult? result;
            try
            {
                result = await _runCycle(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result = null;
            }
            catch (Exception e)
            {
                Trace.TraceError($"Ciclo de sincronização falhou: {e}");
                result = SyncCycleResult.Failed;
            }

            lock (_lock)
            {
                _cts?.Dispose();
                _cts = null;
                _cycleRunning = false;
                if (!_started)
                    return;

                var next = NextWait(result);
                if (_rerunDelay is { } rerun)
                {
                    if (rerun == TimeSpan.Zero)
                        continue;
                    next = rerun < next ? rerun : next;
                }
                _timer.Change(next, Timeout.InfiniteTimeSpan);
                return;
            }
        }
    }

    private TimeSpan NextWait(SyncCycleResult? result)
    {
        switch (result)
        {
            case SyncCycleResult.Failed:
                _failures++;
                var seconds = FirstRetry.TotalSeconds * Math.Pow(2, Math.Min(_failures - 1, 10));
                var wait = TimeSpan.FromSeconds(seconds);
                return wait < MaxRetry ? wait : MaxRetry;
            case SyncCycleResult.Done:
            case SyncCycleResult.Incomplete:
                _failures = 0;
                return Period;
            default: // Skipped, Disabled, Paused e cancelado: não contam como sucesso nem como falha
                return Period;
        }
    }
}
