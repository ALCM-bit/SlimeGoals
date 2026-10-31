using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.Desktop;

internal static class Program
{
    // Nada de Avalonia ou SynchronizationContext antes de AppMain: a plataforma ainda não foi inicializada.
    [STAThread]
    public static void Main(string[] args)
    {
        using var services = BuildServices();
        BuildAvaloniaApp(services).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>Raiz de composição do head Linux (spec §5: DI só nos heads).</summary>
    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        // Ordem de registro = ordem na navegação (spec §7)
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();
        return services.BuildServiceProvider();
    }

    private static AppBuilder BuildAvaloniaApp(IServiceProvider services)
        => AppBuilder.Configure(() => new SlimeGoals.App.App(services))
            .UsePlatformDetect()
            .LogToTrace();
}
