using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
using SlimeGoals.Platform.Linux;
using SlimeGoals.Platform.Linux.Autostart;
using SlimeGoals.Platform.Linux.Notifications;
using SlimeGoals.Platform.Linux.Power;
using SlimeGoals.Platform.Linux.Scheduling;
#if SYNC_DEV
using SlimeGoals.App.Account;
using SlimeGoals.Core.Sync;
using SlimeGoals.Platform.Linux.Secrets;
using SlimeGoals.Platform.Linux.SingleInstance;
using SlimeGoals.Sync;
#endif

namespace SlimeGoals.Desktop;

/// <summary>Raiz de composição do head Linux: único lugar que conhece todas as camadas (spec §5).</summary>
internal static class CompositionRoot
{
    public static ServiceProvider Build(string databasePath, string home, AppStartOptions options)
    {
        var services = new ServiceCollection();

        // Core — os serviços que escrevem recebem o DataChangeNotifier único (parâmetro opcional preenchido pelo container)
#if SYNC_DEV
        services.AddSingleton(PlatformCapabilities.Desktop with { Sync = true });
#else
        services.AddSingleton(PlatformCapabilities.Desktop);
#endif
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
        services.AddSingleton<INotificationBus, TmdsNotificationBus>();
        services.AddSingleton<INotificationService, DbusNotificationService>();
        services.AddSingleton<IReminderScheduler, ProcessTimerReminderScheduler>();

        // Ciclo de vida do app — a mesma instância do ativador é a que a App preenche com a janela
        services.AddSingleton<MainWindowActivator>();
        services.AddSingleton<IWindowActivator>(sp => sp.GetRequiredService<MainWindowActivator>());
        services.AddSingleton<DesktopAppLifetime>();
        services.AddSingleton<IAppLifetime>(sp => sp.GetRequiredService<DesktopAppLifetime>());
        services.AddSingleton(options);
        services.AddSingleton<WindowCloseBehavior>();
        services.AddSingleton<IAppHostedService>(sp => sp.GetRequiredService<WindowCloseBehavior>());
        services.AddSingleton<IAppHostedService, ReminderHost>();
        services.AddSingleton<IAutostart>(_ => new XdgAutostart(
            XdgPaths.AutostartDirectory(Environment.GetEnvironmentVariable, home), Environment.ProcessPath!));
        services.AddSingleton<ISleepSignalSource, LogindSleepSignalSource>();
        services.AddSingleton<SleepResumeWatcher>();
        services.AddSingleton<IAppHostedService, SleepResumeHost>();
#if SYNC_DEV
        AddSync(services, databasePath);
#endif

        // UI — ordem de registro = ordem na navegação (spec §7)
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<TrayMenuViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

#if SYNC_DEV
    private static readonly TimeSpan SyncHttpTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Conta e sincronização, só no build de desenvolvimento (decisões 2 e 14 da 9.5a). O cofre usa como perfil o mesmo
    /// hash do caminho do banco da instância única; o caminho em claro não vai ao chaveiro.
    /// </summary>
    private static void AddSync(IServiceCollection services, string databasePath)
    {
        var profile = SingleInstanceGuard.KeyHash(Path.GetFullPath(databasePath));
        services.AddSingleton<ISecretVault>(_ => SecretServiceVault.ForSessionBus(profile));
        services.AddSingleton(sp => new SyncCoordinator(
            sp.GetRequiredService<ISecretVault>(),
            sp.GetRequiredService<ISyncStateStore>(),
            sp.GetRequiredService<ISyncLocalStore>(),
            sp.GetRequiredService<ReminderService>(),
            sp.GetRequiredService<DataChangeNotifier>(),
            sp.GetRequiredService<TimeProvider>(),
            CreateSyncHttp));
        services.AddSingleton<ISyncAccount>(sp => sp.GetRequiredService<SyncCoordinator>());
        services.AddSingleton(new AccountDefaults(Environment.MachineName));
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<IAppHostedService, SyncHost>();
    }

    /// <summary>
    /// Cliente de um servidor. Não segue redirecionamento: um 3xx para outro host levaria o token junto.
    /// </summary>
    private static HttpClient CreateSyncHttp(Uri server) =>
        new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = server, Timeout = SyncHttpTimeout };
#endif
}
