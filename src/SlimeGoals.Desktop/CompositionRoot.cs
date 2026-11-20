using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
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
using SlimeGoals.Platform.Linux.Secrets;
using SlimeGoals.Platform.Linux.SingleInstance;

namespace SlimeGoals.Desktop;

/// <summary>Raiz de composição do head Linux: único lugar que conhece todas as camadas (spec §5).</summary>
internal static class CompositionRoot
{
    public static ServiceProvider Build(string databasePath, string home, AppStartOptions options)
    {
        var services = new ServiceCollection();

        services.AddSingleton(PlatformCapabilities.Desktop with { Sync = true });
        // Core, formatador dos lembretes e páginas: os mesmos do Android
        services.AddSlimeGoalsApp();

        // Adaptadores
        services.AddSlimeGoalsInfrastructure(databasePath);
        services.AddSingleton<INotificationBus, TmdsNotificationBus>();
        services.AddSingleton<INotificationService, DbusNotificationService>();
        services.AddSingleton<IReminderScheduler, ProcessTimerReminderScheduler>();

        // Aviso de fim de bloco do Pomodoro: alarme próprio no processo e notificação crítica pelo mesmo bus
        services.AddSingleton<ProcessFocusAlarm>();
        services.AddSingleton<IFocusAlarm>(sp => sp.GetRequiredService<ProcessFocusAlarm>());
        services.AddSingleton<DbusFocusAlert>();
        services.AddSingleton<IFocusAlert>(sp => sp.GetRequiredService<DbusFocusAlert>());

        // Ciclo de vida do app — a mesma instância do ativador é a que a App preenche com a janela
        services.AddSingleton<MainWindowActivator>();
        services.AddSingleton<IWindowActivator>(sp => sp.GetRequiredService<MainWindowActivator>());
        services.AddSingleton<DesktopAppLifetime>();
        // O "Sair" com uma sessão de Pomodoro guardada avisa antes que o aviso de fim de bloco não vai tocar
        services.AddSingleton<IAppLifetime>(sp => new FocusQuitNotice(
            sp.GetRequiredService<DesktopAppLifetime>(),
            sp.GetRequiredService<FocusService>(),
            (title, body, ct) => sp.GetRequiredService<INotificationBus>().NotifyAsync(title, body, [], ct: ct),
            sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(options);
        services.AddSingleton<WindowCloseBehavior>();
        services.AddSingleton<IAppHostedService>(sp => sp.GetRequiredService<WindowCloseBehavior>());
        services.AddSingleton<IAppHostedService, ReminderHost>();
        // O alarme e o toque no aviso chegam por threads do relógio e do D-Bus; o host leva a navegação para a interface
        services.AddSingleton<IAppHostedService>(sp =>
        {
            var host = new FocusAlertHost(sp.GetRequiredService<FocusAlerts>(), sp.GetRequiredService<IWindowActivator>(),
                sp.GetRequiredService<PageNavigator>(), action => Dispatcher.UIThread.Post(action));
            sp.GetRequiredService<ProcessFocusAlarm>().Fired += (_, _) => _ = host.FireAsync();
            sp.GetRequiredService<DbusFocusAlert>().Opened += (_, _) => host.Open();
            return host;
        });
        services.AddSingleton<IAutostart>(_ => new XdgAutostart(
            XdgPaths.AutostartDirectory(Environment.GetEnvironmentVariable, home), Environment.ProcessPath!));
        services.AddSingleton<ISleepSignalSource, LogindSleepSignalSource>();
        services.AddSingleton<SleepResumeWatcher>();
        services.AddSingleton<IAppHostedService, SleepResumeHost>();

        // Janelinha do cronômetro: a janela e o ViewModel nascem no primeiro "mostrar" (MiniTimerWindowHost)
        services.AddSingleton<MainWindowPresence>();
        services.AddSingleton<MiniTimerViewModel>();
        services.AddSingleton(sp => new MiniTimerWindowHost(sp.GetRequiredService<MiniTimerViewModel>));
        services.AddSingleton<IAppHostedService>(sp => new MiniTimerController(
            sp.GetRequiredService<RunningTimerWatcher>(),
            sp.GetRequiredService<MainWindowPresence>(),
            sp.GetRequiredService<MiniTimerWindowHost>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<DataChangeNotifier>(),
            action => Dispatcher.UIThread.Post(action)));
        AddSync(services, databasePath);

        services.AddSingleton<TrayMenuViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>
    /// Conta e sincronização em todos os builds. O cofre usa como perfil o mesmo hash do caminho do banco da instância
    /// única; o caminho em claro não vai ao chaveiro. Só o build de desenvolvimento sugere um servidor (o de teste local);
    /// no Release o campo começa vazio, com a dica.
    /// </summary>
    private static void AddSync(IServiceCollection services, string databasePath)
    {
        var profile = SingleInstanceGuard.KeyHash(Path.GetFullPath(databasePath));
#if SYNC_DEV
        var defaultServer = new Uri("http://127.0.0.1:8080");
#else
        Uri? defaultServer = null;
#endif
        services.AddSlimeGoalsSync(_ => SecretServiceVault.ForSessionBus(profile), Environment.MachineName, defaultServer);
    }
}
