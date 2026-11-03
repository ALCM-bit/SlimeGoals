using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;

namespace SlimeGoals.Android;

/// <summary>Raiz de composição do head Android: espelha a do desktop, sem o que é só de desktop/Linux.</summary>
internal static class AndroidCompositionRoot
{
    public static ServiceProvider Build(string databasePath)
    {
        var services = new ServiceCollection();

        // Core — os serviços que escrevem recebem o DataChangeNotifier único
        services.AddSingleton(new PlatformCapabilities(false, false, false, false));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ReminderPause>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<ReminderResponseHandler>();

        // Adaptadores
        services.AddSlimeGoalsInfrastructure(databasePath);
        services.AddSingleton<IReminderFormatter, ReminderFormatter>();
        services.AddSingleton<INotificationService, NoopNotificationService>();

        // UI — ordem de registro = ordem na navegação
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
