using System.Diagnostics;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Diagnostics;

namespace SlimeGoals.Platform.Linux.Notifications;

/// <summary>
/// Aviso de fim de bloco do Pomodoro no GNOME (spec Pomodoro §4.5, Linux): uma notificação de urgência crítica, que o
/// GNOME mostra mesmo no "Não perturbe" e mantém até ser dispensada, com o som de alarme do tema. Só o título vai na
/// notificação, como na tela de alarme do Android: nada de meta nem tarefa. Mostrar outro fecha o anterior antes;
/// tocar no aviso dispara <see cref="Opened"/>. As chamadas chegam de qualquer thread e vão ao D-Bus uma de cada vez,
/// na ordem em que chegaram; uma falha vai ao Trace e não impede a próxima.
/// </summary>
public sealed class DbusFocusAlert : IFocusAlert, IDisposable
{
    /// <summary>O som de alarme do tema de sons do freedesktop.</summary>
    public const string Sound = "alarm-clock-elapsed";

    private static readonly IReadOnlyList<(string Key, string Label)> Actions = [("default", "Abrir")];

    private readonly INotificationBus _bus;
    private readonly Lock _lock = new();
    private Task _pending = Task.CompletedTask;
    private uint? _shown;

    public DbusFocusAlert(INotificationBus bus)
    {
        _bus = bus;
        _bus.ActionInvoked += OnActionInvoked;
        _bus.NotificationClosed += OnNotificationClosed;
    }

    /// <summary>A pessoa tocou no aviso. Chega pela thread do D-Bus.</summary>
    public event EventHandler? Opened;

    /// <summary>Termina quando tudo que foi pedido até agora chegou ao D-Bus (para testes e diagnóstico). Nunca falha.</summary>
    public Task Pending
    {
        get
        {
            lock (_lock)
                return _pending;
        }
    }

    /// <inheritdoc />
    public void Show(FocusAlertKind kind)
    {
        var title = FocusAlertText.Of(kind);
        Enqueue(async () =>
        {
            await CloseShownAsync().ConfigureAwait(false);
            var id = await _bus.NotifyAsync(title, "", Actions, critical: true, soundName: Sound).ConfigureAwait(false);
            lock (_lock)
                _shown = id;
        }, "mostrar o aviso do Pomodoro");
    }

    /// <summary>Não faz nada: o som do tema toca uma vez só, junto com a notificação.</summary>
    public void Silence()
    {
    }

    /// <inheritdoc />
    public void Withdraw() => Enqueue(CloseShownAsync, "retirar o aviso do Pomodoro");

    /// <summary>Desassina o bus (o bus em si pertence a quem o criou).</summary>
    public void Dispose()
    {
        _bus.ActionInvoked -= OnActionInvoked;
        _bus.NotificationClosed -= OnNotificationClosed;
    }

    private async Task CloseShownAsync()
    {
        uint id;
        lock (_lock)
        {
            if (_shown is not { } shown)
                return;
            id = shown;
            _shown = null;
        }
        await _bus.CloseAsync(id).ConfigureAwait(false);
    }

    /// <summary>Encadeia a operação depois das anteriores, para um Withdraw não passar na frente do Show que o precede.</summary>
    private void Enqueue(Func<Task> operation, string what)
    {
        lock (_lock)
            _pending = RunAfterAsync(_pending, operation, what);
    }

    private static async Task RunAfterAsync(Task previous, Func<Task> operation, string what)
    {
        await previous.ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao {what}: {TraceText.Of(e)}");
        }
    }

    private void OnActionInvoked(uint id, string key)
    {
        if (key != "default")
            return;
        lock (_lock)
            if (_shown != id)
                return;
        try
        {
            Opened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha em assinante do toque no aviso do Pomodoro: {TraceText.Of(e)}");
        }
    }

    private void OnNotificationClosed(uint id, uint reason)
    {
        lock (_lock)
            if (_shown == id)
                _shown = null;
    }
}
