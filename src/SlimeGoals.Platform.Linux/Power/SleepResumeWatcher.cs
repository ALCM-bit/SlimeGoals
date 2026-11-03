using System.Diagnostics;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Platform.Linux.Power;

/// <summary>
/// Ao acordar da suspensão avisa o <see cref="DataChangeNotifier"/>: o agendador recalcula os lembretes
/// e a tela Hoje recarrega. Falha ao iniciar a fonte: registra em Trace e segue sem.
/// </summary>
public sealed class SleepResumeWatcher(ISleepSignalSource source, DataChangeNotifier changes) : IAsyncDisposable
{
    private int _disposed;

    public async Task StartAsync(CancellationToken ct = default)
    {
        try
        {
            await source.StartAsync(OnPrepareForSleep, ct).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Não foi possível observar a suspensão: {e}");
        }
    }

    private void OnPrepareForSleep(bool sleeping)
    {
        if (sleeping || Volatile.Read(ref _disposed) == 1) return;
        try
        {
            changes.NotifyChanged();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao tratar o despertar da suspensão: {e}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao encerrar a observação da suspensão: {e}");
        }
    }
}
