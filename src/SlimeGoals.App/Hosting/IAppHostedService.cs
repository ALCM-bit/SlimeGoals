namespace SlimeGoals.App.Hosting;

/// <summary>Serviço que vive junto com o app: a <see cref="App"/> o inicia depois de criar a janela e o encerra no fim.</summary>
public interface IAppHostedService
{
    /// <summary>Chamado sem bloquear a interface, depois que a janela principal existe.</summary>
    Task StartAsync();

    /// <summary>Chamado no encerramento, fora da thread da interface e com prazo curto.</summary>
    Task StopAsync();
}
