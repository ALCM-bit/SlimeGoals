using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using SlimeGoals.Core.Abstractions;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// O aviso de fim de bloco do Pomodoro como despertador (spec Pomodoro §4.5). Canal próprio "Pomodoro", de importância
/// alta, com o som de alarme do aparelho no uso de alarme (toca no volume de alarme, no silencioso e no "Não perturbe"
/// com alarmes liberados) e vibração. A notificação é insistente (o som repete) e de categoria alarme, e acende a tela
/// com a <see cref="FocusAlarmActivity"/> sobre a tela de bloqueio. Ela só mostra a fase: nada de meta, tarefa ou tempo.
/// Tocar abre a aba Tempo; dispensar, ou passar um minuto, cala o som, e o aviso fica na central até o toque.
/// </summary>
internal sealed class AndroidFocusAlert(Context context) : IFocusAlert
{
    public const string ChannelId = "pomodoro";
    public const string ExtraOpenTempo = "openTempo";

    // Os lembretes usam o 1 e o cronômetro, o 2.
    private const int NotificationId = 3;
    // O alarme do fim de bloco usa o 3 (AndroidFocusAlarm).
    private const int SilenceRequestCode = 4;
    private const int DismissRequestCode = 5;
    private const int OpenRequestCode = 12;
    private const int FullScreenRequestCode = 13;

    /// <summary>Grupo sem resumo do aviso calado.</summary>
    private const string SilentGroup = "pomodoro_silent";

    /// <summary>Quanto o som toca sem ninguém mexer.</summary>
    private static readonly TimeSpan RingFor = TimeSpan.FromMinutes(1);

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    private AlarmManager Alarms => (AlarmManager)context.GetSystemService(Context.AlarmService)!;

    public void Show(FocusAlertKind kind)
    {
        EnsureChannel();
        Volatile.Write(ref s_ringing, true);
        Manager.Notify(NotificationId, Build(FocusAlertText.Of(kind), kind));
        ArmSilence();
    }

    /// <summary>
    /// Se um aviso mostrado por este processo ainda toca. Vale na hora, ao contrário da central, que o sistema atualiza
    /// depois; serve para a tela de alarme não fechar por um silêncio que um aviso novo já superou.
    /// </summary>
    internal static bool RingingHere => Volatile.Read(ref s_ringing);

    private static bool s_ringing;

    /// <summary>
    /// Publica o mesmo aviso de novo, sem repetir o som e sem alertar outra vez. Lê o aviso à mostra (o processo pode
    /// ser outro, acordado pelo alarme do silêncio); sem aviso tocando, só desarma o silêncio.
    /// </summary>
    /// <remarks>
    /// Atualizar a notificação sem <c>FLAG_INSISTENT</c> não para o som que já toca (conferido na API 36): só retirá-la
    /// para. Por isso ela sai e volta, calada (<see cref="Build"/>), e a central não chega a ficar sem o aviso para a
    /// pessoa. Retirar pelo app não manda o <c>deleteIntent</c>.
    /// </remarks>
    public void Silence()
    {
        Alarms.Cancel(SilenceIntent());
        Volatile.Write(ref s_ringing, false);
        FocusAlarmActivity.CloseShown();
        if (Shown(Manager) is not { } shown || (shown.Flags & NotificationFlags.Insistent) == 0)
            return;
        var silenced = Build(shown.Extras?.GetString(Notification.ExtraTitle) ?? "", ringing: null);
        Manager.Cancel(NotificationId);
        Manager.Notify(NotificationId, silenced);
    }

    public void Withdraw()
    {
        Alarms.Cancel(SilenceIntent());
        Volatile.Write(ref s_ringing, false);
        FocusAlarmActivity.CloseShown();
        Manager.Cancel(NotificationId);
    }

    /// <summary>Se o aviso está na central e ainda toca (insistente).</summary>
    internal static bool IsRinging(Context context)
        => Shown((NotificationManager)context.GetSystemService(Context.NotificationService)!) is { } shown
            && (shown.Flags & NotificationFlags.Insistent) != 0;

    /// <summary>O toque leva à <see cref="MainActivity"/>, que abre a aba Tempo (e cala o som).</summary>
    internal static PendingIntent OpenTempo(Context context, int requestCode)
        => PendingIntent.GetActivity(context, requestCode, OpenTempoIntent(context),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

    internal static Intent OpenTempoIntent(Context context)
        => new Intent(context, typeof(MainActivity))
            .SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop)
            .PutExtra(ExtraOpenTempo, true);

    // Criar um canal que já existe não faz nada (nem desfaz o que a pessoa mudou nele): som e vibração só valem na
    // primeira vez.
    private void EnsureChannel()
    {
        var channel = new NotificationChannel(ChannelId, "Pomodoro", NotificationImportance.High);
        channel.SetSound(RingtoneManager.GetDefaultUri(RingtoneType.Alarm), new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Alarm)!
            .SetContentType(AudioContentType.Sonification)!
            .Build());
        channel.EnableVibration(true);
        channel.LockscreenVisibility = NotificationVisibility.Public;
        Manager.CreateNotificationChannel(channel);
    }

    /// <param name="ringing">O aviso que toca, com a tela de alarme; nulo, o aviso já calado.</param>
    private Notification Build(string title, FocusAlertKind? ringing)
    {
        var builder = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(AndroidNotificationService.Accent)
            .SetContentTitle(title)
            .SetCategory(Notification.CategoryAlarm)
            // Só a fase, então pode aparecer inteira na tela de bloqueio.
            .SetVisibility(NotificationVisibility.Public)
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(ringing is null)
            .SetContentIntent(OpenTempo(context, OpenRequestCode))
            .SetDeleteIntent(FocusAlarmReceiver.Broadcast(context, DismissRequestCode, FocusAlarmReceiver.ActionSilence));
        // Sem a permissão de tela cheia, o Android mostra o aviso no topo da tela, com o mesmo som.
        if (ringing is { } kind)
            builder.SetFullScreenIntent(FullScreen(kind), true);
        // O aviso calado volta como notificação nova, não como atualização: o "alertar uma vez" não basta para ele não
        // tocar de novo. Um filho de grupo que só alerta pelo resumo (e o grupo não tem resumo) não toca, como no
        // setSilent do NotificationCompat.
        else
            builder.SetGroup(SilentGroup).SetGroupAlertBehavior(NotificationGroupAlertBehavior.Summary);
        var notification = builder.Build();
        if (ringing is not null)
            notification.Flags |= NotificationFlags.Insistent;
        return notification;
    }

    private PendingIntent FullScreen(FocusAlertKind kind)
    {
        var intent = new Intent(context, typeof(FocusAlarmActivity))
            .SetFlags(ActivityFlags.NewTask | ActivityFlags.NoUserAction)
            .PutExtra(FocusAlarmActivity.ExtraKind, kind.ToString());
        return PendingIntent.GetActivity(context, FullScreenRequestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    /// <summary>O silêncio daqui a um minuto, no relógio desde o boot: mudar a hora do aparelho não o desloca.</summary>
    private void ArmSilence()
    {
        var alarms = Alarms;
        var triggerAt = SystemClock.ElapsedRealtime() + (long)RingFor.TotalMilliseconds;
        if (!OperatingSystem.IsAndroidVersionAtLeast(31) || alarms.CanScheduleExactAlarms())
        {
            try
            {
                alarms.SetExactAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, SilenceIntent());
                return;
            }
            catch (Java.Lang.SecurityException)
            {
                // Permissão revogada entre a checagem e a chamada: cai para o alarme inexato.
            }
        }
        alarms.SetAndAllowWhileIdle(AlarmType.ElapsedRealtimeWakeup, triggerAt, SilenceIntent());
    }

    private PendingIntent SilenceIntent()
        => FocusAlarmReceiver.Broadcast(context, SilenceRequestCode, FocusAlarmReceiver.ActionSilence);

    private static Notification? Shown(NotificationManager manager)
        => manager.GetActiveNotifications()?.FirstOrDefault(n => n.Id == NotificationId && n.Tag is null)?.Notification;
}
