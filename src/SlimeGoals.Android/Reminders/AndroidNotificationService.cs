using Android.App;
using Android.Content;
using Android.Graphics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using AUri = Android.Net.Uri;

namespace SlimeGoals.Android.Reminders;

/// <summary>
/// Notificações de lembrete do Android. Cada uma tem a tag = <c>LogId</c>; os botões e o arrastar para o lado
/// vão ao <see cref="ReminderActionReceiver"/>, que trata a resposta mesmo com o processo morto. Por isso
/// <see cref="ActionReceived"/> nunca dispara.
/// </summary>
internal sealed class AndroidNotificationService(Context context) : INotificationService
{
    public const string ChannelId = "lembretes";
    public const string ExtraLogId = "logId";
    public const string ExtraAction = "action";
    public const string TimerConflictText = "Há um cronômetro aberto. Toque para abrir o app.";

    private const int NotificationId = 1;
    private static readonly Color Accent = Color.ParseColor("#9B7BEA");

    private NotificationManager Manager => (NotificationManager)context.GetSystemService(Context.NotificationService)!;

    /// <inheritdoc />
    public event EventHandler<ReminderActionEventArgs>? ActionReceived
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task ShowAsync(Reminder reminder, CancellationToken ct = default)
    {
        EnsureChannel();
        var logId = reminder.LogId;
        var notification = NewBuilder()
            .SetContentTitle(reminder.Text.Title)
            .SetContentText(reminder.Text.Body)
            .SetCategory(Notification.CategoryReminder)
            .SetDeleteIntent(ActionIntent(logId, ReminderAction.Ignore))
            .AddAction(Button(reminder.Text.PrimaryLabel, ActionIntent(logId, ReminderAction.Primary)))
            .AddAction(Button("Adiar", ActionIntent(logId, ReminderAction.Snooze)))
            .AddAction(Button("Ignorar", ActionIntent(logId, ReminderAction.Ignore)))
            .Build();
        Manager.Notify(Tag(logId), NotificationId, notification);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task WithdrawAsync(Guid logId, CancellationToken ct = default)
    {
        Manager.Cancel(Tag(logId), NotificationId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Troca o lembrete pelo aviso de que outro cronômetro está rodando: sem botões, o toque abre o app.
    /// Mantém o título do lembrete, se ele ainda estiver na tela.
    /// </summary>
    public void ShowTimerConflict(Guid logId)
    {
        EnsureChannel();
        var tag = Tag(logId);
        var title = Manager.GetActiveNotifications()?
            .FirstOrDefault(n => n.Tag == tag && n.Id == NotificationId)?
            .Notification?.Extras?.GetCharSequence(Notification.ExtraTitle);
        var builder = NewBuilder()
            .SetContentText(TimerConflictText)
            .SetDeleteIntent(ActionIntent(logId, ReminderAction.Ignore))
            .SetOnlyAlertOnce(true);
        if (title is not null)
            builder.SetContentTitle(title);
        Manager.Notify(tag, NotificationId, builder.Build());
    }

    private static string Tag(Guid logId) => logId.ToString("N");

    // Criar um canal que já existe não faz nada (nem desfaz o que o usuário mudou nele).
    private void EnsureChannel()
        => Manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Lembretes", NotificationImportance.High));

    private Notification.Builder NewBuilder()
        => new Notification.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_notification)
            .SetColor(Accent)
            .SetContentIntent(OpenAppIntent())
            .SetAutoCancel(true);

    private static Notification.Action Button(string label, PendingIntent intent)
        => new Notification.Action.Builder((global::Android.Graphics.Drawables.Icon?)null, label, intent).Build();

    // O Data único por registro e ação impede que um PendingIntent substitua o de outro botão ou lembrete.
    private PendingIntent ActionIntent(Guid logId, ReminderAction action)
    {
        var intent = new Intent(context, typeof(ReminderActionReceiver))
            .SetData(AUri.Parse($"slimegoals://reminder/{Tag(logId)}/{action}"))
            .PutExtra(ExtraLogId, logId.ToString())
            .PutExtra(ExtraAction, action.ToString());
        return PendingIntent.GetBroadcast(context, 0, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }

    private PendingIntent OpenAppIntent()
    {
        var intent = new Intent(context, typeof(MainActivity))
            .SetFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, 0, intent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
    }
}
