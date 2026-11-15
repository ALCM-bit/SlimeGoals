namespace SlimeGoals.App.Views;

/// <summary>
/// Fim do arrasto da janelinha. O gerenciador de janelas move a janela sozinho, sem avisar quando a pessoa solta:
/// a posição que fica parada por <see cref="Delay"/> vale como soltura. Uma pausa no meio do arrasto grava a posição
/// da pausa, e o arrasto continua valendo: a posição em que a pessoa solta é gravada depois.
/// </summary>
public sealed class MiniTimerDrop(TimeProvider time, Action<(int X, int Y)> dropped)
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(400);

    private readonly Lock _lock = new();
    private ITimer? _timer;
    private (int X, int Y) _last;
    private bool _dragging;

    /// <summary>O arrasto começou: as próximas posições contam.</summary>
    public void Begin()
    {
        lock (_lock)
            _dragging = true;
    }

    /// <summary>A janela mudou de lugar.</summary>
    public void Moved((int X, int Y) position)
    {
        lock (_lock)
        {
            if (!_dragging)
                return;
            _last = position;
            _timer?.Dispose();
            _timer = time.CreateTimer(_ => Settled(), null, Delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Encerra o arrasto sem gravar nada pendente.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _dragging = false;
            _timer?.Dispose();
            _timer = null;
        }
    }

    // Roda na thread do timer; quem recebe a posição despacha para a interface.
    private void Settled()
    {
        (int X, int Y) position;
        lock (_lock)
        {
            if (!_dragging)
                return;
            position = _last;
        }
        dropped(position);
    }
}
