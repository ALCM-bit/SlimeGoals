using System.Diagnostics;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Platform.Linux;
using SlimeGoals.Platform.Linux.SingleInstance;

namespace SlimeGoals.Desktop;

internal static class Program
{
    // Nada de Avalonia ou SynchronizationContext antes de AppMain: a plataforma ainda não foi inicializada.
    [STAThread]
    public static void Main(string[] args)
    {
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
                SingleInstanceGuard.SignalExisting(runtime, key);
                return;
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
            using var services = CompositionRoot.Build(databasePath);
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

            BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
        }
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new SlimeGoals.App.App(services))
            .UsePlatformDetect()
            .LogToTrace();
}
