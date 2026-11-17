using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Platform.Linux.Scheduling;

/// <summary>
/// Alarme do fim de bloco do Pomodoro que vive no processo, separado do agendador dos lembretes: UM temporizador, que
/// dispara <see cref="Fired"/> uma vez no instante armado. Armar de novo substitui o anterior. O relógio dos
/// temporizadores para na suspensão; na volta, o aviso de mudança do <c>SleepResumeWatcher</c> faz o
/// <c>FocusAlerts</c> rearmar pelo relógio de parede.
/// </summary>
public sealed class ProcessFocusAlarm(TimeProvider time) : IFocusAlarm, IDisposable
{
    private readonly Lock _lock = new();
    private ITimer? _timer;
    private long _armed; // conta cada Set, Cancel e Dispose: um disparo de um alarme já trocado não vale
    private bool _disposed;

    /// <summary>O alarme tocou. Chega pela thread do temporizador.</summary>
    public event EventHandler? Fired;

    /// <inheritdoc />
    public void Set(DateTimeOffset at)
    {
        ITimer timer;
        long armed;
        lock (_lock)
        {
            if (_disposed)
                return;
            _timer?.Dispose();
            armed = ++_armed;
            // Criado parado e ligado fora da trava: um relógio que dispara na hora não chama o assinante com ela presa.
            _timer = timer = time.CreateTimer(_ => OnTimer(armed), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        var due = at - time.GetUtcNow();
        try
        {
            timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Outro Set ou um Cancel chegou entre a trava e aqui: esse alarme já não vale.
        }
    }

    /// <inheritdoc />
    public void Cancel()
    {
        lock (_lock)
        {
            _armed++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _armed++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void OnTimer(long armed)
    {
        lock (_lock)
        {
            if (armed != _armed)
                return;
            _armed++; // toca uma vez só
            _timer?.Dispose();
            _timer = null;
        }
        try
        {
            Fired?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha no disparo do alarme do Pomodoro: {TraceText.Of(e)}");
        }
    }
}
