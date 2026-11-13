using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Platform.Linux.Notifications;

/// <summary>
/// Notificações de lembrete sobre o <see cref="INotificationBus"/>: guarda o par id ↔ LogId e traduz
/// ações e fechamentos em <see cref="ActionReceived"/>.
/// </summary>
public sealed class DbusNotificationService : INotificationService, IDisposable
{
    private sealed class Entry(Guid logId) { public Guid LogId { get; } = logId; public bool Answered { get; set; } }

    private readonly INotificationBus _bus;
    private readonly Lock _lock = new();
    private readonly Dictionary<uint, Entry> _byId = [];
    private readonly Dictionary<Guid, uint> _byLogId = [];
    private bool? _bodyMarkup;

    public DbusNotificationService(INotificationBus bus)
    {
        _bus = bus;
        _bus.ActionInvoked += OnActionInvoked;
        _bus.NotificationClosed += OnNotificationClosed;
    }

    /// <inheritdoc />
    public event EventHandler<ReminderActionEventArgs>? ActionReceived;

    /// <inheritdoc />
    public async Task ShowAsync(Reminder reminder, CancellationToken ct = default)
    {
        IReadOnlyList<(string Key, string Label)> actions =
        [
            ("primary", reminder.Text.PrimaryLabel),
            ("snooze", "Adiar"),
            ("ignore", "Ignorar"),
            ("default", "Abrir"),
        ];
        var body = await BodyMarkupAsync(ct).ConfigureAwait(false) ? Escape(reminder.Text.Body) : reminder.Text.Body;
        var id = await _bus.NotifyAsync(reminder.Text.Title, body, actions, ct).ConfigureAwait(false);
        lock (_lock)
        {
            _byId[id] = new Entry(reminder.LogId);
            _byLogId[reminder.LogId] = id;
        }
    }

    /// <summary>
    /// Pergunta ao servidor uma vez. Se a pergunta falhar, o lembrete sai com o corpo sem escape e a próxima
    /// notificação pergunta de novo: um texto mal formatado é melhor que um lembrete perdido.
    /// </summary>
    private async Task<bool> BodyMarkupAsync(CancellationToken ct)
    {
        if (_bodyMarkup is { } known) return known;
        try
        {
            var supported = await _bus.SupportsBodyMarkupAsync(ct).ConfigureAwait(false);
            _bodyMarkup = supported;
            return supported;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Trace.TraceError($"Falha ao consultar as capacidades do servidor de notificações: {TraceText.Of(e)}");
            return false;
        }
    }

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <inheritdoc />
    public async Task WithdrawAsync(Guid logId, CancellationToken ct = default)
    {
        uint id;
        lock (_lock)
        {
            if (!_byLogId.TryGetValue(logId, out id)) return;
            Remove(id);
        }
        await _bus.CloseAsync(id, ct).ConfigureAwait(false);
    }

    private void Remove(uint id)
    {
        if (_byId.Remove(id, out var entry)) _byLogId.Remove(entry.LogId);
    }

    private void OnActionInvoked(uint id, string key)
    {
        try
        {
            ReminderAction? action = key switch
            {
                "primary" => ReminderAction.Primary,
                "snooze" => ReminderAction.Snooze,
                "ignore" => ReminderAction.Ignore,
                "default" => ReminderAction.Open,
                _ => null,
            };
            if (action is null) return;

            Guid logId;
            lock (_lock)
            {
                if (!_byId.TryGetValue(id, out var entry)) return;
                entry.Answered = true;
                logId = entry.LogId;
            }
            Raise(logId, action.Value);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao tratar ActionInvoked: {TraceText.Of(ex)}");
        }
    }

    private void OnNotificationClosed(uint id, uint reason)
    {
        try
        {
            Guid logId;
            lock (_lock)
            {
                if (!_byId.TryGetValue(id, out var entry)) return;
                Remove(id);
                if (entry.Answered || reason is not (1 or 2)) return;
                logId = entry.LogId;
            }
            Raise(logId, ReminderAction.Ignore);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha ao tratar NotificationClosed: {TraceText.Of(ex)}");
        }
    }

    private void Raise(Guid logId, ReminderAction action)
    {
        try
        {
            ActionReceived?.Invoke(this, new ReminderActionEventArgs(logId, action));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Falha em assinante de ActionReceived: {TraceText.Of(ex)}");
        }
    }

    /// <summary>Desassina o bus (o bus em si pertence a quem o criou).</summary>
    public void Dispose()
    {
        _bus.ActionInvoked -= OnActionInvoked;
        _bus.NotificationClosed -= OnNotificationClosed;
    }
}
