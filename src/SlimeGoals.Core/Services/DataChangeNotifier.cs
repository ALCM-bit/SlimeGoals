namespace SlimeGoals.Core.Services;

/// <summary>
/// Avisa que os dados mudaram (meta, registro, cronômetro, resposta de lembrete). Quem exibe dados
/// (tela Hoje) ou agenda lembretes assina <see cref="Changed"/> e recarrega.
/// </summary>
public sealed class DataChangeNotifier
{
    /// <summary>Disparado depois de cada escrita bem-sucedida; pode ocorrer em qualquer thread.</summary>
    public event EventHandler? Changed;

    public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
