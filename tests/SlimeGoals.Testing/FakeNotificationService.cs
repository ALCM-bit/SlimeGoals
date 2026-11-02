using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Notificador falso: guarda o que foi mostrado e retirado, e pode ser configurado para falhar.</summary>
public sealed class FakeNotificationService : INotificationService
{
    private readonly List<Reminder> _shown = [];
    private readonly List<Guid> _withdrawn = [];

    public IReadOnlyList<Reminder> Shown => _shown;
    public IReadOnlyList<Guid> Withdrawn => _withdrawn;

    /// <summary>Quando definido, <see cref="ShowAsync"/> lança esta exceção (e não registra o lembrete).</summary>
    public Exception? ShowFailsWith { get; set; }

    public event EventHandler<ReminderActionEventArgs>? ActionReceived;

    public Task ShowAsync(Reminder reminder, CancellationToken ct = default)
    {
        if (ShowFailsWith is not null) throw ShowFailsWith;
        _shown.Add(reminder);
        return Task.CompletedTask;
    }

    public Task WithdrawAsync(Guid logId, CancellationToken ct = default)
    {
        _withdrawn.Add(logId);
        return Task.CompletedTask;
    }

    /// <summary>Simula o usuário acionando uma notificação.</summary>
    public void RaiseAction(Guid logId, ReminderAction action) =>
        ActionReceived?.Invoke(this, new ReminderActionEventArgs(logId, action));
}
