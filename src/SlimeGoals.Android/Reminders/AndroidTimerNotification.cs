using Android.App;
using Android.Content;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// Notificação fixa com o cronômetro rodando: o próprio sistema desenha o tempo (<c>SetUsesChronometer</c>), então
/// nenhum serviço fica ligado para atualizá-la. Canal "Cronômetro", de importância baixa: sem som nem vibração, e a
/// pessoa pode desligá-lo sem perder os lembretes. Tocar abre o app; "Parar" abre o app com
/// <see cref="ExtraStopTimer"/>, e a <see cref="MainActivity"/> para o cronômetro. Desde o Android 12, um receiver não
/// pode abrir uma Activity a partir de uma notificação, por isso o "Parar" não passa por um.
/// </summary>
internal sealed class AndroidTimerNotification(Context context) : ITimerNotification
{
    public const string ChannelId = "cronometro";
    public const string ExtraStopTimer = "stopTimer";

    // Os lembretes usam o 1, com a tag do registro.
    private const int NotificationId = 2;
    private const int OpenRequestCode = 10;
    private const int StopRequestCode = 11;

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    public void Show(RunningTimer timer)
    {
        // Criar um canal que já existe não faz nada (nem desfaz o que a pessoa mudou nele).
        Manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Cronômetro", NotificationImportance.Low));
        var stop = new Notification.Action.Builder((global::Android.Graphics.Drawables.Icon?)null, "Parar",
            ActivityIntent(StopRequestCode, stop: true)).Build();
        var notification = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(AndroidNotificationService.Accent)
            .SetContentTitle(timer.Title)
            .SetCategory(OperatingSystem.IsAndroidVersionAtLeast(31) ? Notification.CategoryStopwatch : Notification.CategoryProgress)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(true)
            .SetUsesChronometer(true)
            .SetWhen(timer.StartedAt.ToUnixTimeMilliseconds())
            .SetContentIntent(ActivityIntent(OpenRequestCode, stop: false))
            .AddAction(stop)
            .Build();
        Manager.Notify(NotificationId, notification);
    }

    public void Cancel() => Manager.Cancel(NotificationId);

    // Códigos de pedido diferentes: os dois intents só diferem no extra, que não conta para o PendingIntent ser outro.
    private PendingIntent ActivityIntent(int requestCode, bool stop)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        if (stop)
            intent.PutExtra(ExtraStopTimer, true);
        return PendingIntent.GetActivity(context, requestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
