using Android.App;
using Android.Content;
using Android.OS;
using Android.Text.Format;
using Android.Views;
using Android.Widget;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// Notificação fixa com o cronômetro rodando: o próprio sistema desenha o tempo (<c>SetUsesChronometer</c>), então
/// nenhum serviço fica ligado para atualizá-la. Canal "Cronômetro", de importância baixa: sem som nem vibração, e a
/// pessoa pode desligá-lo sem perder os lembretes. Tocar abre o app; "Parar" abre o app com
/// <see cref="ExtraStopTimer"/> valendo o código da instalação (<see cref="StopTimerSecretStore"/>), e a
/// <see cref="MainActivity"/> para o cronômetro se o código conferir. Desde o Android 12, um receiver não pode abrir
/// uma Activity a partir de uma notificação, por isso o "Parar" não passa por um; e como a Activity é exportada, o
/// código impede que outro app peça para parar.
/// A sessão de Pomodoro (<see cref="RunningTimer.Focus"/>) usa a mesma notificação, sem o "Parar": o tempo conta para
/// baixo até o fim do bloco e, pausado ou à espera do toque, fica parado.
/// </summary>
internal sealed class AndroidTimerNotification(Context context, StopTimerSecretStore secrets) : ITimerNotification
{
    public const string ChannelId = "cronometro";
    public const string ExtraStopTimer = "stopTimer";

    // Os lembretes usam o 1, com a tag do registro.
    private const int NotificationId = 2;
    private const int OpenRequestCode = 10;
    private const int StopRequestCode = 11;
    private const int OpenTempoRequestCode = 15;

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    public void Show(RunningTimer timer)
    {
        // Criar um canal que já existe não faz nada (nem desfaz o que a pessoa mudou nele).
        Manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Cronômetro", NotificationImportance.Low));
        // Sem o código (preferências com falha), o botão não funcionaria: a notificação fica só com o toque. A sessão de
        // Pomodoro termina só pela aba Tempo.
        var stop = timer.Focus is null && secrets.Get() is { } secret
            ? new Notification.Action.Builder((global::Android.Graphics.Drawables.Icon?)null, "Parar",
                ActivityIntent(StopRequestCode, secret)).Build()
            : null;
        // Privada: na tela de bloqueio que esconde o conteúdo sensível (configuração do sistema), o Android mostra a
        // versão pública, igual mas sem meta nem tarefa (RunningTimer.PublicLabel).
        var notification = Builder(timer, timer.Label, stop)
            .SetVisibility(NotificationVisibility.Private)
            .SetPublicVersion(Builder(timer, timer.PublicLabel, stop).Build())
            .Build();
        Manager.Notify(NotificationId, notification);
    }

    public void Cancel() => Manager.Cancel(NotificationId);

    private Notification.Builder Builder(RunningTimer timer, string label, Notification.Action? stop)
    {
        var builder = new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(AndroidNotificationService.Accent)
            .SetContentTitle(label) // leitor de tela e lugares que não mostram o layout próprio
            .SetCategory(OperatingSystem.IsAndroidVersionAtLeast(31) ? Notification.CategoryStopwatch : Notification.CategoryProgress)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            // O tempo vai grande no corpo; o do cabeçalho (pequeno, ao lado do nome do app) sai para não repetir.
            .SetShowWhen(false)
            .SetStyle(new Notification.DecoratedCustomViewStyle())
            .SetCustomContentView(Body(Resource.Layout.notification_timer, timer, label))
            .SetCustomBigContentView(Body(Resource.Layout.notification_timer_big, timer, label))
            // A sessão de Pomodoro abre na aba Tempo.
            .SetContentIntent(timer.Focus is null
                ? ActivityIntent(OpenRequestCode, secret: null)
                : AndroidFocusAlert.OpenTempo(context, OpenTempoRequestCode));
        if (stop is not null)
            builder.AddAction(stop);
        return builder;
    }

    // O Chronometer conta no relógio desde o boot; ao mudar a hora do aparelho, o SystemEventReceiver repõe a notificação.
    private RemoteViews Body(int layout, RunningTimer timer, string label)
    {
        var views = new RemoteViews(context.PackageName, layout);
        views.SetTextViewText(Resource.Id.timer_title, label);
        var now = DateTimeOffset.UtcNow;
        if (timer.Focus is not { } focus)
            views.SetChronometer(Resource.Id.timer_elapsed,
                ChronometerBase.Of(timer.StartedAt, now, SystemClock.ElapsedRealtime()), null, true);
        else if (focus.PausedRemaining is null && focus.EndsAt > now)
        {
            views.SetChronometer(Resource.Id.timer_elapsed,
                ChronometerBase.CountdownOf(focus.EndsAt, now, SystemClock.ElapsedRealtime()), null, true);
            views.SetChronometerCountDown(Resource.Id.timer_elapsed, true);
        }
        else
        {
            // Um Chronometer parado relê o relógio cada vez que a notificação é desenhada: o texto fixo não anda.
            views.SetViewVisibility(Resource.Id.timer_elapsed, ViewStates.Gone);
            views.SetViewVisibility(Resource.Id.timer_fixed, ViewStates.Visible);
            views.SetTextViewText(Resource.Id.timer_fixed, Fixed(focus.PausedRemaining ?? TimeSpan.Zero));
        }
        return views;
    }

    /// <summary>O tempo que falta no formato do Chronometer ("24:10", "1:05:00"), com os segundos para cima.</summary>
    private static string Fixed(TimeSpan remaining)
        => DateUtils.FormatElapsedTime(Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds))) ?? "";

    // Códigos de pedido diferentes: os dois intents só diferem no extra, que não conta para o PendingIntent ser outro.
    // Imutável: quem recebe a notificação não troca o extra.
    private PendingIntent ActivityIntent(int requestCode, string? secret)
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        if (secret is not null)
            intent.PutExtra(ExtraStopTimer, secret);
        return PendingIntent.GetActivity(context, requestCode, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
