namespace SlimeGoals.Core.Services;

/// <summary>
/// Avisa que os dados mudaram (meta, registro, cronômetro, resposta de lembrete). Quem exibe dados
/// (tela Hoje) ou agenda lembretes assina <see cref="Changed"/> e recarrega.
/// </summary>
public sealed class DataChangeNotifier
{
    private readonly object _lock = new();
    private int _deferrals;
    private bool _pending;

    /// <summary>Disparado depois de cada escrita bem-sucedida; pode ocorrer em qualquer thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Chama cada assinante isolado: a falha de um é registrada e não afeta os demais nem quem escreveu.
    /// Com um <see cref="Defer"/> aberto, só anota a mudança; o aviso sai quando o último escopo fecha.
    /// </summary>
    public void NotifyChanged()
    {
        lock (_lock)
        {
            if (_deferrals > 0)
            {
                _pending = true;
                return;
            }
        }
        Raise();
    }

    /// <summary>
    /// Abre um escopo em que os avisos ficam retidos (aninhável). Ao fechar o último escopo aberto,
    /// <see cref="Changed"/> é disparado uma vez se alguma mudança foi anotada nesse meio-tempo.
    /// </summary>
    public IDisposable Defer()
    {
        lock (_lock)
            _deferrals++;
        return new Scope(this);
    }

    private void EndDeferral()
    {
        lock (_lock)
        {
            if (--_deferrals > 0 || !_pending)
                return;
            _pending = false;
        }
        Raise();
    }

    private void Raise()
    {
        if (Changed is not { } handlers)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.TraceError($"Assinante de DataChangeNotifier falhou: {e}");
            }
        }
    }

    /// <summary>Fecha uma única vez, mesmo se descartado de novo.</summary>
    private sealed class Scope(DataChangeNotifier owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.EndDeferral();
        }
    }
}
