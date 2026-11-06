namespace SlimeGoals.Core.Sync;

/// <summary>
/// Porta pela qual o motor aplica exclusões recebidas do servidor sem deixar notificações órfãs: quem a
/// implementa lê as pendentes antes, espera os disparos em andamento e retira as das metas apagadas depois.
/// </summary>
public interface IRemoteDeletionGate
{
    /// <summary>
    /// Executa <paramref name="apply"/>, que devolve os ids das metas apagadas, e retira as notificações
    /// pendentes delas. Se <paramref name="apply"/> falhar, nada é retirado e a exceção segue adiante.
    /// </summary>
    Task RunAsync(Func<CancellationToken, Task<IReadOnlyList<Guid>>> apply, CancellationToken ct = default);
}
