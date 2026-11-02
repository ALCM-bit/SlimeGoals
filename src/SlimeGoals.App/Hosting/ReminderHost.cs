using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// Liga os lembretes ao ciclo de vida do app: inicia o agendador, responde às ações das notificações
/// (trazendo a janela quando o Core pede) e expira os pendentes no encerramento. Só depende de portas
/// e serviços do Core, então serve a qualquer head. O agendador e o bus são descartados pelo container.
/// </summary>
public sealed class ReminderHost(IReminderScheduler scheduler, INotificationService notifications,
    ReminderResponseHandler handler, ReminderService reminders, IWindowActivator window) : IAppHostedService
{
    private int _subscribed;

    public async Task StartAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) == 0)
            notifications.ActionReceived += OnActionReceived;
        try
        {
            await scheduler.StartAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // O app segue sem lembretes (decisão 5 do plano): nada de derrubar a janela.
            Trace.TraceError($"Falha ao iniciar os lembretes: {e}");
        }
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _subscribed, 0) == 1)
            notifications.ActionReceived -= OnActionReceived;
        await reminders.ExpirePendingAsync().ConfigureAwait(false);
    }

    // Chega por threads do D-Bus: toda exceção fica aqui e vai para o Trace.
    private async void OnActionReceived(object? sender, ReminderActionEventArgs e)
    {
        try
        {
            var outcome = await handler.HandleAsync(e.LogId, e.Action).ConfigureAwait(false);
            if (outcome == ReminderOutcome.OpenApp)
                window.Activate();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao tratar a ação {e.Action} do lembrete {e.LogId}: {ex}");
        }
    }
}
