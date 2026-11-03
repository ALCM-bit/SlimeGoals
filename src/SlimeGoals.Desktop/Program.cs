using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Platform.Linux;
using SlimeGoals.Platform.Linux.SingleInstance;

namespace SlimeGoals.Desktop;

internal static class Program
{
    private static readonly TimeSpan AcquireRetryTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AcquireRetryInterval = TimeSpan.FromMilliseconds(200);

    // Nada de Avalonia ou SynchronizationContext antes de AppMain: a plataforma ainda não foi inicializada.
    [STAThread]
    public static void Main(string[] args)
    {
        var options = AppStartOptions.Parse(args);
        InputMethodEnvironment.Apply(Environment.GetEnvironmentVariable, Environment.SetEnvironmentVariable);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var databasePath = XdgPaths.DatabasePath(Environment.GetEnvironmentVariable, home);

        // Instância única por banco: a segunda só pede à primeira que mostre a janela e sai, sem abrir o banco.
        var runtime = XdgPaths.RuntimeDirectory(Environment.GetEnvironmentVariable, Environment.UserName, Path.GetTempPath());
        var key = Path.GetFullPath(databasePath);
        SingleInstanceGuard? guard;
        try
        {
            guard = SingleInstanceGuard.TryAcquire(runtime, key);
            if (guard is null)
            {
                // Autostart duplicado: sai sem pedir a janela.
                if (options.Background)
                    return;
                if (SingleInstanceGuard.SignalExisting(runtime, key))
                    return;
                // Trava presa sem resposta: a outra está encerrando (ou travou). Espera um pouco para assumir.
                guard = SingleInstanceGuard.WaitToAcquire(runtime, key, AcquireRetryTimeout, AcquireRetryInterval);
                if (guard is null)
                {
                    Trace.TraceWarning("Outra instância segura a trava e não responde; saindo.");
                    return;
                }
            }
        }
        catch (Exception e)
        {
            // Pasta ou socket indisponível: o app segue sem instância única em vez de não abrir.
            Trace.TraceError($"Falha ao garantir instância única; seguindo sem ela: {e}");
            guard = null;
        }

        using (guard)
        {
            using var services = CompositionRoot.Build(databasePath, home, options);
            using (var connection = services.GetRequiredService<SqliteConnectionFactory>().Open())
                MigrationRunner.Migrate(connection);

            if (guard is not null)
            {
                var activator = services.GetRequiredService<MainWindowActivator>();
                guard.ShowRequested += (_, _) =>
                {
                    try
                    {
                        activator.Activate();
                    }
                    catch (Exception e)
                    {
                        Trace.TraceError($"Falha ao mostrar a janela a pedido da segunda instância: {e}");
                    }
                };
            }

            BuildAvaloniaApp(services, guard).StartWithClassicDesktopLifetime(args);
        }
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services, SingleInstanceGuard? guard)
        => AppBuilder.Configure(() => new SlimeGoals.App.App(services))
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "slimegoals" })
            .LogToTrace()
            .AfterSetup(builder => StopListeningOnExit(builder, guard));

    /// <summary>
    /// Ao sair, para de ouvir já no <c>Exit</c>, antes de encerrar os serviços: uma nova abertura nesse meio-tempo
    /// não recebe <c>ok</c> de quem está indo embora e espera a trava, que só sai no <c>using</c> do Main
    /// (depois de fechar o banco).
    /// </summary>
    private static void StopListeningOnExit(AppBuilder builder, SingleInstanceGuard? guard)
    {
        if (guard is null)
            return;
        if (builder.Instance?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Exit += (_, _) => guard.StopListening();
        else
            Trace.TraceWarning("Sem ciclo de vida de desktop no AfterSetup; a instância única só é liberada no fim do Main.");
    }
}
