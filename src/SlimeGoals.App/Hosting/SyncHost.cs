using SlimeGoals.Sync;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Liga a conta ao ciclo de vida do app: inicia o coordenador (que, havendo sessão, liga o motor) e o para no
/// encerramento, dentro do prazo curto que a <see cref="App"/> dá a cada serviço.
/// </summary>
public sealed class SyncHost(ISyncAccount account) : IAppHostedService
{
    public Task StartAsync() => account.StartAsync();

    public Task StopAsync() => account.StopAsync();
}
