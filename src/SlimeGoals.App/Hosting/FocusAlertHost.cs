using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Liga o aviso de fim de bloco do Pomodoro ao app: inicia o <see cref="FocusAlerts"/> junto com os outros serviços, e o
/// head liga o disparo do alarme a <see cref="FireAsync"/> e o toque no aviso a <see cref="Open"/>. Só depende do Core e
/// do app, então serve a qualquer head. O <see cref="FocusAlerts"/> é descartado pelo container.
/// </summary>
/// <param name="dispatch">Leva uma ação à thread da interface.</param>
public sealed class FocusAlertHost(FocusAlerts alerts, IWindowActivator window, PageNavigator navigator,
    Action<Action> dispatch) : IAppHostedService
{
    public Task StartAsync() => alerts.StartAsync();

    public Task StopAsync() => Task.CompletedTask;

    /// <summary>O alarme tocou; chega pela thread do relógio. Nunca lança.</summary>
    public Task FireAsync() => alerts.FireAsync();

    /// <summary>
    /// A pessoa tocou no aviso; chega de qualquer thread. Cala o som e, na thread da interface, abre o app na aba Tempo.
    /// </summary>
    public void Open()
    {
        alerts.Silence();
        dispatch(() =>
        {
            navigator.Show<TimePageViewModel>();
            window.Activate();
        });
    }
}
