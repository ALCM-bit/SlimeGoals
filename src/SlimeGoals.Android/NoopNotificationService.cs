using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Android;

/// <summary>
/// Notificações que não fazem nada: só satisfazem o <c>ReminderService</c>, usado pela tela Hoje.
/// A fase 7b troca isto pelo <c>AlarmManager</c> e notificações reais.
/// </summary>
internal sealed class NoopNotificationService : INotificationService
{
    public event EventHandler<ReminderActionEventArgs>? ActionReceived
    {
        add { }
        remove { }
    }

    public Task ShowAsync(Reminder reminder, CancellationToken ct = default) => Task.CompletedTask;

    public Task WithdrawAsync(Guid logId, CancellationToken ct = default) => Task.CompletedTask;
}
