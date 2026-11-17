using System.Diagnostics;
using Android.App;
using Android.Content;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// O alarme do Pomodoro: <see cref="ActionFire"/> é o fim do bloco (<see cref="AndroidFocusAlarm"/>), que acorda o app,
/// lê a sessão guardada e mostra o aviso; <see cref="ActionSilence"/> cala o som, vindo do minuto do aviso ou de
/// dispensar a notificação (<see cref="AndroidFocusAlert"/>). Não exportado: só os PendingIntents do app chegam aqui.
/// </summary>
[BroadcastReceiver(Name = "dev.alcm.slimegoals.FocusAlarmReceiver", Enabled = true, Exported = false)]
public sealed class FocusAlarmReceiver : BroadcastReceiver
{
    public const string ActionFire = "fire";
    public const string ActionSilence = "silence";

    public override void OnReceive(Context? context, Intent? intent)
    {
        switch (intent?.Action)
        {
            case ActionFire:
                ReceiverWork.Run(this, context!, services => services.GetRequiredService<FocusAlerts>().FireAsync());
                break;
            case ActionSilence:
                ReceiverWork.Run(this, context!, services =>
                {
                    services.GetRequiredService<IFocusAlert>().Silence();
                    return Task.CompletedTask;
                });
                break;
            default:
                Trace.TraceError($"Ação do alarme do Pomodoro inválida: {intent?.Action}");
                break;
        }
    }

    /// <summary>
    /// O PendingIntent de uma ação deste receiver. Imutável; com <c>UpdateCurrent</c>, o mesmo código e a mesma ação dão
    /// o mesmo PendingIntent, que serve para armar de novo e para cancelar.
    /// </summary>
    internal static PendingIntent Broadcast(Context context, int requestCode, string action)
        => PendingIntent.GetBroadcast(context, requestCode, new Intent(context, typeof(FocusAlarmReceiver)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
