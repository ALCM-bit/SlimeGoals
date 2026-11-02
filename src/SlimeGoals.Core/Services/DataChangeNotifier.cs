namespace SlimeGoals.Core.Services;

/// <summary>
/// Avisa que os dados mudaram (meta, registro, cronômetro, resposta de lembrete). Quem exibe dados
/// (tela Hoje) ou agenda lembretes assina <see cref="Changed"/> e recarrega.
/// </summary>
public sealed class DataChangeNotifier
{
    /// <summary>Disparado depois de cada escrita bem-sucedida; pode ocorrer em qualquer thread.</summary>
    public event EventHandler? Changed;

    /// <summary>Chama cada assinante isolado: a falha de um é registrada e não afeta os demais nem quem escreveu.</summary>
    public void NotifyChanged()
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
}
