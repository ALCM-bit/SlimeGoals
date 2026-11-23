using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Account;
using SlimeGoals.App.Goals;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync;
using SlimeGoals.Sync.Integrity;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Os registros iguais nos dois heads. Quem chama e monta o contêiner continua sendo o head (spec §5); o que é de uma
/// plataforma só, como as notificações, o agendador e o cofre, fica lá.
/// </summary>
public static class AppServiceCollectionExtensions
{
    /// <summary>
    /// Os serviços do Core, o formatador dos lembretes e as páginas. Os serviços que escrevem recebem o
    /// <see cref="DataChangeNotifier"/> único (parâmetro opcional preenchido pelo contêiner).
    /// </summary>
    public static IServiceCollection AddSlimeGoalsApp(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<TagService>();
        services.AddSingleton<TagsSettingsViewModel>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<FocusService>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<ReminderPause>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<ReminderResponseHandler>();
        services.AddSingleton<IReminderFormatter, ReminderFormatter>();
        services.AddSingleton<FocusAlerts>();
        services.AddSingleton<RunningTimerWatcher>();

        // Pedidos de troca de página feitos por dentro das páginas (ex.: Tempo → Hoje); o shell assina
        services.AddSingleton<PageNavigator>();

        // Ordem de registro = ordem na navegação (spec §7)
        services.AddSingleton<PageViewModel, TodayPageViewModel>();
        services.AddSingleton<PageViewModel, GoalsPageViewModel>();
        services.AddSingleton<PomodoroViewModel>();
        services.AddSingleton<PageViewModel, TimePageViewModel>();
        services.AddSingleton<PageViewModel, ReportsPageViewModel>();
        services.AddSingleton<PageViewModel, SettingsPageViewModel>();
        services.AddSingleton<ShellViewModel>();
        return services;
    }

    /// <summary>
    /// Conta e sincronização, que os dois heads chamam em todos os builds. Um coordenador só atende a tela de conta e o
    /// <see cref="SyncHost"/>. Um <see cref="ITextClipboard"/> registrado pelo head chega à tela de conta, e um
    /// <see cref="IAppIntegrity"/>, ao coordenador e à tela.
    /// <paramref name="defaultServer"/> é o servidor que o campo da tela de conta sugere (nulo deixa o campo vazio).
    /// </summary>
    public static IServiceCollection AddSlimeGoalsSync(this IServiceCollection services,
        Func<IServiceProvider, ISecretVault> vault, string deviceName, Uri? defaultServer)
    {
        services.AddSingleton<ISecretVault>(vault);
        services.AddSingleton(sp => new SyncCoordinator(
            sp.GetRequiredService<ISecretVault>(),
            sp.GetRequiredService<ISyncStateStore>(),
            sp.GetRequiredService<ISyncLocalStore>(),
            sp.GetRequiredService<ReminderService>(),
            sp.GetRequiredService<DataChangeNotifier>(),
            sp.GetRequiredService<TimeProvider>(),
            SyncHttp.Create,
            sp.GetService<IAppIntegrity>()));
        services.AddSingleton<ISyncAccount>(sp => sp.GetRequiredService<SyncCoordinator>());
        services.AddSingleton(new AccountDefaults(deviceName, defaultServer));
        services.AddSingleton<AccountViewModel>();
        services.AddSingleton<IAppHostedService, SyncHost>();
        return services;
    }
}
