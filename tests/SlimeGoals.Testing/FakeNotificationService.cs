using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Notificador falso: guarda o que foi mostrado e retirado, e pode ser configurado para falhar.</summary>
public sealed class FakeNotificationService : INotificationService
{
    private readonly List<Reminder> _shown = [];
    private readonly List<Guid> _withdrawn = [];
    private readonly List<Guid> _visible = [];

    public IReadOnlyList<Reminder> Shown => _shown;
    public IReadOnlyList<Guid> Withdrawn => _withdrawn;

    /// <summary>O que está na central agora: retirar antes de mostrar não adianta, como na vida real.</summary>
    public IReadOnlyList<Guid> Visible => _visible;

    /// <summary>Quando definido, <see cref="ShowAsync"/> lança esta exceção (e não registra o lembrete).</summary>
    public Exception? ShowFailsWith { get; set; }

    /// <summary>Quando definido, <see cref="WithdrawAsync"/> lança esta exceção (e não retira nada).</summary>
    public Exception? WithdrawFailsWith { get; set; }

    /// <summary>Concluído quando <see cref="ShowAsync"/> começa.</summary>
    public TaskCompletionSource? ShowStarted { get; set; }

    /// <summary>Segura o <see cref="ShowAsync"/> até ser concluído (simula a chamada ao sistema em andamento).</summary>
    public TaskCompletionSource? ShowGate { get; set; }

    public event EventHandler<ReminderActionEventArgs>? ActionReceived;

    public async Task ShowAsync(Reminder reminder, CancellationToken ct = default)
    {
        if (ShowFailsWith is not null) throw ShowFailsWith;
        ShowStarted?.TrySetResult();
        if (ShowGate is not null) await ShowGate.Task;
        _shown.Add(reminder);
        _visible.Add(reminder.LogId);
    }

    public Task WithdrawAsync(Guid logId, CancellationToken ct = default)
    {
        if (WithdrawFailsWith is not null) throw WithdrawFailsWith;
        _withdrawn.Add(logId);
        _visible.Remove(logId);
        return Task.CompletedTask;
    }

    /// <summary>Simula o usuário acionando uma notificação.</summary>
    public void RaiseAction(Guid logId, ReminderAction action) =>
        ActionReceived?.Invoke(this, new ReminderActionEventArgs(logId, action));
}
