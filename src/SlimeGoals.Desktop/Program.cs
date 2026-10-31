using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Platform.Linux;

namespace SlimeGoals.Desktop;

internal static class Program
{
    // Nada de Avalonia ou SynchronizationContext antes de AppMain: a plataforma ainda não foi inicializada.
    [STAThread]
    public static void Main(string[] args)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var databasePath = XdgPaths.DatabasePath(Environment.GetEnvironmentVariable, home);

        using var services = CompositionRoot.Build(databasePath);
        using (var connection = services.GetRequiredService<SqliteConnectionFactory>().Open())
            MigrationRunner.Migrate(connection);

        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new SlimeGoals.App.App(services))
            .UsePlatformDetect()
            .LogToTrace();
}
