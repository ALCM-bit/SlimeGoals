using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Argumentos de uma ação recebida de uma notificação.</summary>
public sealed class ReminderActionEventArgs(Guid logId, ReminderAction action) : EventArgs
{
    public Guid LogId { get; } = logId;
    public ReminderAction Action { get; } = action;
}

/// <summary>Porta de saída que mostra notificações de lembrete (implementada na Linux/Desktop).</summary>
public interface INotificationService
{
    Task ShowAsync(Reminder reminder, CancellationToken ct = default);

    /// <summary>Retira da central de notificações; no-op se o id não está aberto.</summary>
    Task WithdrawAsync(Guid logId, CancellationToken ct = default);

    /// <summary>O usuário acionou uma notificação (botão ou corpo).</summary>
    event EventHandler<ReminderActionEventArgs>? ActionReceived;
}
