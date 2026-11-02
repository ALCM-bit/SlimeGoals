using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
using SlimeGoals.Platform.Linux.Notifications;
using SlimeGoals.Platform.Linux.Scheduling;

namespace SlimeGoals.Desktop;

/// <summary>Raiz de composição do head Linux: único lugar que conhece todas as camadas (spec §5).</summary>
internal static class CompositionRoot
{
    public static ServiceProvider Build(string databasePath)
    {
        var services = new ServiceCollection();

        // Core — os serviços que escrevem recebem o DataChangeNotifier único (parâmetro opcional preenchido pelo container)
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<ReminderPause>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<ReminderResponseHandler>();

        // Adaptadores
        services.AddSlimeGoalsInfrastructure(databasePath);
        services.AddSingleton<IReminderFormatter, ReminderFormatter>();
        services.AddSingleton<INotificationBus, TmdsNotificationBus>();
        services.AddSingleton<INotificationService, DbusNotificationService>();
        services.AddSingleton<IReminderScheduler, ProcessTimerReminderScheduler>();

        // Ciclo de vida do app — a mesma instância do ativador é a que a App preenche com a janela
        services.AddSingleton<MainWindowActivator>();
        services.AddSingleton<IWindowActivator>(sp => sp.GetRequiredService<MainWindowActivator>());
        services.AddSingleton<DesktopAppLifetime>();
        services.AddSingleton<IAppLifetime>(sp => sp.GetRequiredService<DesktopAppLifetime>());
        services.AddSingleton<IAppHostedService, ReminderHost>();

        // UI — ordem de registro = ordem na navegação (spec §7)
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<TrayMenuViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
