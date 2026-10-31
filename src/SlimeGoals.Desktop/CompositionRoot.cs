using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;

namespace SlimeGoals.Desktop;

/// <summary>Raiz de composição do head Linux: único lugar que conhece todas as camadas (spec §5).</summary>
internal static class CompositionRoot
{
    public static ServiceProvider Build(string databasePath)
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<GoalService>();

        // Adaptadores
        services.AddSlimeGoalsInfrastructure(databasePath);

        // UI — ordem de registro = ordem na navegação (spec §7)
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
