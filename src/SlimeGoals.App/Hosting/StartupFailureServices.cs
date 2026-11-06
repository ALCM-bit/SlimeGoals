using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Container de quando o banco não abriu: leva só a tela de erro. A <see cref="App"/> o reconhece pelo
/// <see cref="StartupFailureViewModel"/> e não resolve shell nem serviços, que dependem do banco.
/// </summary>
public static class StartupFailureServices
{
    public static ServiceProvider Create(string databasePath) => new ServiceCollection()
        .AddSingleton(new StartupFailureViewModel(databasePath))
        .AddSingleton<MainWindowActivator>()
        .BuildServiceProvider();
}
