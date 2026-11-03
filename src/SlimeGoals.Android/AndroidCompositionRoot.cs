using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Android.Reminders;
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
    public static ServiceProvider Build(Context context, string databasePath)
    {
        var services = new ServiceCollection();

        // Core — os serviços que escrevem recebem o DataChangeNotifier único
        services.AddSingleton(new PlatformCapabilities(true, false, false, false));
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
        services.AddSingleton<AndroidNotificationService>(_ => new AndroidNotificationService(context));
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<AndroidNotificationService>());
        services.AddSingleton<ISystemAlarm>(_ => new AndroidSystemAlarm(context));
        services.AddSingleton<AndroidReminderPermissions>(_ => new AndroidReminderPermissions(context));
        services.AddSingleton<IReminderPermissions>(sp => sp.GetRequiredService<AndroidReminderPermissions>());

        // Lembretes — a mesma instância do agendador atende o ReminderHost e os receivers
        services.AddSingleton<SystemAlarmReminderScheduler>();
        services.AddSingleton<IReminderScheduler>(sp => sp.GetRequiredService<SystemAlarmReminderScheduler>());
        services.AddSingleton<IWindowActivator, NoopWindowActivator>();
        services.AddSingleton<IAppHostedService, ReminderHost>();

        // UI — ordem de registro = ordem na navegação
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
