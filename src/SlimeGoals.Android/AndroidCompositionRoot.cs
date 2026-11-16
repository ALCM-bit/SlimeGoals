using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Android.Reminders;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
#if SYNC_DEV
using SlimeGoals.Android.Sync;
using SlimeGoals.App.Account;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync;
#endif

namespace SlimeGoals.Android;

/// <summary>Raiz de composição do head Android: espelha a do desktop, sem o que é só de desktop/Linux.</summary>
internal static class AndroidCompositionRoot
{
    public static ServiceProvider Build(Context context, string databasePath)
    {
        var services = new ServiceCollection();

        // Core — os serviços que escrevem recebem o DataChangeNotifier único
#if SYNC_DEV
        services.AddSingleton(new PlatformCapabilities(true, false, false, false, Sync: true));
#else
        services.AddSingleton(new PlatformCapabilities(true, false, false, false));
#endif
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<FocusService>();
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

        // Cronômetro na notificação — ligado no AndroidApplication.OnCreate, para valer também sem a tela aberta
        services.AddSingleton<RunningTimerWatcher>();
        services.AddSingleton(_ => new StopTimerSecretStore(context));
        services.AddSingleton<ITimerNotification>(sp
            => new AndroidTimerNotification(context, sp.GetRequiredService<StopTimerSecretStore>()));
        services.AddSingleton<TimerNotificationSync>();

#if SYNC_DEV
        AddSync(services, context);
#endif

        // Pedidos de troca de página feitos por dentro das páginas (ex.: Tempo → Hoje); o shell assina
        services.AddSingleton<PageNavigator>();

        // UI — ordem de registro = ordem na navegação
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PomodoroViewModel>();
        services.AddSingleton<PageViewModel, TimePageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

#if SYNC_DEV
    private static readonly TimeSpan SyncHttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Conta e sincronização, só no APK de desenvolvimento (9.5b): cofre sobre o Keystore, nome do aparelho do sistema e a
    /// área de transferência que marca o código de recuperação como sensível. O resto espelha o head Linux.
    /// </summary>
    private static void AddSync(IServiceCollection services, Context context)
    {
        services.AddSingleton<ISecretVault>(_ => new AndroidKeystoreVault(context));
        services.AddSingleton(sp => new SyncCoordinator(
            sp.GetRequiredService<ISecretVault>(),
            sp.GetRequiredService<ISyncStateStore>(),
            sp.GetRequiredService<ISyncLocalStore>(),
            sp.GetRequiredService<ReminderService>(),
            sp.GetRequiredService<DataChangeNotifier>(),
            sp.GetRequiredService<TimeProvider>(),
            CreateSyncHttp));
        services.AddSingleton<ISyncAccount>(sp => sp.GetRequiredService<SyncCoordinator>());
        services.AddSingleton(new AccountDefaults(AndroidDeviceName.Get(context)));
        services.AddSingleton(sp => new AccountViewModel(
            sp.GetRequiredService<ISyncAccount>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<AccountDefaults>(),
            platformClipboard: new AndroidSensitiveClipboard(context)));
        services.AddSingleton<IAppHostedService, SyncHost>();
    }

    /// <summary>
    /// Cliente de um servidor. Não segue redirecionamento: um 3xx para outro host levaria o token junto. O texto puro
    /// só passa para 127.0.0.1 e localhost (network_security_config_dev).
    /// </summary>
    private static HttpClient CreateSyncHttp(Uri server) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = server, Timeout = SyncHttpTimeout };
#endif
}
