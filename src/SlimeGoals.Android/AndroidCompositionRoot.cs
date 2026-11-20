using Android.Content;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Android.Reminders;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure;
#if SYNC_DEV
using SlimeGoals.Android.Sync;
using SlimeGoals.App.Account;
#endif

namespace SlimeGoals.Android;

/// <summary>Raiz de composição do head Android: espelha a do desktop, sem o que é só de desktop/Linux.</summary>
internal static class AndroidCompositionRoot
{
    public static ServiceProvider Build(Context context, string databasePath)
    {
        var services = new ServiceCollection();

#if SYNC_DEV
        services.AddSingleton(PlatformCapabilities.Android with { Sync = true });
#else
        services.AddSingleton(PlatformCapabilities.Android);
#endif
        // Core, formatador dos lembretes e páginas: os mesmos do desktop
        services.AddSlimeGoalsApp();

        // Adaptadores
        services.AddSlimeGoalsInfrastructure(databasePath);
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
        services.AddSingleton(_ => new StopTimerSecretStore(context));
        services.AddSingleton<ITimerNotification>(sp
            => new AndroidTimerNotification(context, sp.GetRequiredService<StopTimerSecretStore>()));
        services.AddSingleton<TimerNotificationSync>();

        // Aviso de fim de bloco do Pomodoro: alarme de despertador e notificação no canal "Pomodoro". Ligado no
        // AndroidApplication.OnCreate; o alarme chega pelo FocusAlarmReceiver e o toque no aviso, pela MainActivity.
        services.AddSingleton<IFocusAlarm>(_ => new AndroidFocusAlarm(context));
        services.AddSingleton<IFocusAlert>(_ => new AndroidFocusAlert(context));
        services.AddSingleton(sp => new FocusAlertHost(sp.GetRequiredService<FocusAlerts>(),
            sp.GetRequiredService<IWindowActivator>(), sp.GetRequiredService<PageNavigator>(),
            action => Dispatcher.UIThread.Post(action)));

#if SYNC_DEV
        AddSync(services, context);
#endif

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

#if SYNC_DEV
    /// <summary>
    /// Conta e sincronização, só no APK de desenvolvimento (9.5b): cofre sobre o Keystore, nome do aparelho do sistema e a
    /// área de transferência que marca o código de recuperação como sensível. O resto é o mesmo do head Linux.
    /// </summary>
    private static void AddSync(IServiceCollection services, Context context)
    {
        services.AddSingleton<ITextClipboard>(_ => new AndroidSensitiveClipboard(context));
        services.AddSlimeGoalsSync(_ => new AndroidKeystoreVault(context), AndroidDeviceName.Get(context));
    }
#endif
}
