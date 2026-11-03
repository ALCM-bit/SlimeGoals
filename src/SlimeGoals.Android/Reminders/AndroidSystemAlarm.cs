using Android.App;
using System.Diagnostics;
using Android.Content;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// O alarme único do <c>SystemAlarmReminderScheduler</c> sobre o <see cref="AlarmManager"/>: sobrevive ao processo
/// e, ao tocar, chama o <see cref="ReminderAlarmReceiver"/>. Sem a permissão de alarme exato, cai para um alarme
/// inexato (o Android pode atrasá-lo).
/// </summary>
internal sealed class AndroidSystemAlarm(Context context) : ISystemAlarm
{
    // Código fixo: o mesmo PendingIntent (com UpdateCurrent) substitui o alarme anterior e serve para cancelá-lo.
    private const int RequestCode = 1;

    private AlarmManager Alarms => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    public void Set(DateTimeOffset at)
    {
        var alarms = Alarms;
        var triggerAt = at.ToUnixTimeMilliseconds();
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
        {
            try
            {
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, CreatePendingIntent());
                return;
            }
            catch (Java.Lang.SecurityException e)
            {
                // Permissão revogada entre a checagem e a chamada: cai para o alarme inexato.
                Trace.TraceWarning($"Alarme exato negado, usando inexato: {e.Message}");
            }
        }
        alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt, CreatePendingIntent());
    }

    public void Cancel() => Alarms.Cancel(CreatePendingIntent());

    private PendingIntent CreatePendingIntent()
        => PendingIntent.GetBroadcast(context, RequestCode, new Intent(context, typeof(ReminderAlarmReceiver)),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
}
