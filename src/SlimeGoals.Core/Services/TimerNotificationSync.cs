using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Mantém a notificação do cronômetro igual ao <see cref="RunningTimerWatcher"/>: mostra enquanto há
/// cronômetro rodando e retira quando não há.
/// </summary>
public sealed class TimerNotificationSync(RunningTimerWatcher watcher, ITimerNotification notification)
{
    private readonly Lock _lock = new();
    private int _subscribed;

    /// <summary>Assina o watcher (uma vez só), inicia-o e sincroniza. Nunca lança.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            watcher.Changed += (_, _) => Apply();
        await watcher.StartAsync(ct);
        Apply();
    }

    /// <summary>Relê o cronômetro e mostra ou retira a notificação. Nunca lança.</summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        await watcher.RefreshAsync(ct);
        Apply();
    }

    // O valor é lido dentro da trava: entre duas chamadas simultâneas, a última deixa a notificação certa.
    private void Apply()
    {
        lock (_lock)
        {
            try
            {
                if (watcher.Current is { } timer)
                    notification.Show(timer);
                else
                    notification.Cancel();
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha ao atualizar a notificação do cronômetro: {TraceText.Of(e)}");
            }
        }
    }
}
