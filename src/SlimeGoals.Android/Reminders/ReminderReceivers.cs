using System.Diagnostics;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>Trabalho assíncrono num <see cref="BroadcastReceiver"/>, com o container do processo.</summary>
internal static class ReceiverWork
{
    /// <summary>Prazo do trabalho: abaixo dos ~10 s que o Android dá a um receiver.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Roda <paramref name="work"/> fora da thread principal com <c>GoAsync()</c>; falha ou estouro do prazo
    /// vão para o Trace, e o <c>Finish()</c> é sempre chamado.
    /// </summary>
    public static void Run(BroadcastReceiver receiver, Context context, Func<IServiceProvider, Task> work)
    {
        var pending = receiver.GoAsync();
        var name = receiver.GetType().Name;
        _ = Task.Run(async () =>
        {
            try
            {
                // O AndroidApplication.OnCreate monta o container antes de qualquer receiver.
                var services = AndroidApplication.Services
                    ?? throw new InvalidOperationException("Container do processo ainda não montado.");
                await work(services).WaitAsync(Timeout).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha no receiver {name}: {e}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

/// <summary>O alarme do sistema tocou: reagenda, o que mostra os lembretes devidos e arma o próximo.</summary>
[BroadcastReceiver(Name = "dev.alcm.slimegoals.ReminderAlarmReceiver", Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
        => ReceiverWork.Run(this, context!, services
            => services.GetRequiredService<SystemAlarmReminderScheduler>().FiredAsync());
}

/// <summary>Um botão da notificação (ou o arrastar para o lado): responde ao lembrete sem abrir tela.</summary>
[BroadcastReceiver(Name = "dev.alcm.slimegoals.ReminderActionReceiver", Enabled = true, Exported = false)]
public sealed class ReminderActionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (!Guid.TryParse(intent?.GetStringExtra(AndroidNotificationService.ExtraLogId), out var logId)
            || !Enum.TryParse<ReminderAction>(intent?.GetStringExtra(AndroidNotificationService.ExtraAction), out var action))
        {
            Trace.TraceError($"Ação de lembrete inválida: {intent?.DataString}");
            return;
        }

        ReceiverWork.Run(this, context!, async services =>
        {
            var outcome = await services.GetRequiredService<ReminderResponseHandler>()
                .HandleAsync(logId, action).ConfigureAwait(false);
            var notifications = services.GetRequiredService<AndroidNotificationService>();
            if (outcome == ReminderOutcome.OpenApp)
                notifications.ShowTimerConflict(logId);
            else
                await notifications.WithdrawAsync(logId).ConfigureAwait(false);
            await services.GetRequiredService<SystemAlarmReminderScheduler>().RescheduleAsync().ConfigureAwait(false);
        });
    }
}
