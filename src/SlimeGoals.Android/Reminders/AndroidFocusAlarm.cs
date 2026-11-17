using System.Diagnostics;
using Android.App;
using Android.Content;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// O alarme do fim de bloco do Pomodoro (spec Pomodoro §4.5), separado do alarme dos lembretes: armado como despertador
/// (<c>setAlarmClock</c>), o tipo que a economia de bateria mais respeita, e ao tocar chama o
/// <see cref="FocusAlarmReceiver"/>. Sem a permissão de alarme exato, cai para um alarme inexato, como os lembretes.
/// </summary>
internal sealed class AndroidFocusAlarm(Context context) : IFocusAlarm
{
    // Os lembretes usam o 1; o silêncio do aviso, o 4.
    private const int RequestCode = 3;
    private const int ShowRequestCode = 14;

    private AlarmManager Alarms => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    public void Set(DateTimeOffset at)
    {
        var alarms = Alarms;
        var triggerAt = at.ToUnixTimeMilliseconds();
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
        {
            try
            {
                // O toque no despertador da barra de status (e da tela de bloqueio) abre a aba Tempo.
                var show = AndroidFocusAlert.OpenTempo(context, ShowRequestCode);
                alarms.SetAlarmClock(new AlarmManager.AlarmClockInfo(triggerAt, show), CreatePendingIntent());
                return;
            }
            catch (Java.Lang.SecurityException e)
            {
                // Permissão revogada entre a checagem e a chamada: cai para o alarme inexato.
                Trace.TraceWarning($"Alarme exato do Pomodoro negado, usando inexato: {TraceText.Types(e)}");
            }
        }
        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, CreatePendingIntent());
    }

    public void Cancel() => Alarms.Cancel(CreatePendingIntent());

    // Código fixo: o mesmo PendingIntent (com UpdateCurrent) substitui o alarme anterior e serve para cancelá-lo.
    private PendingIntent CreatePendingIntent() => FocusAlarmReceiver.Broadcast(context, RequestCode, FocusAlarmReceiver.ActionFire);
}
