using SlimeGoals.Platform.Linux.Notifications;

namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>Uma notificação enviada ao <see cref="FakeNotificationBus"/>, com as dicas de urgência e de som.</summary>
internal sealed record SentNotification(uint Id, string Summary, string Body, IReadOnlyList<(string Key, string Label)> Actions,
    bool Critical, string? SoundName);

/// <summary>
/// <see cref="INotificationBus"/> em memória: ids a partir de 1, cada envio e fechamento anotado em ordem em
/// <see cref="Log"/> ("notify 1", "close 1"), e os sinais disparados à mão.
/// </summary>
internal sealed class FakeNotificationBus : INotificationBus
{
    private readonly Lock _lock = new();
    private uint _next;

    public List<SentNotification> Notified { get; } = [];
    public List<uint> Closed { get; } = [];
    public List<string> Log { get; } = [];
    public bool BodyMarkup { get; init; }
    public Exception? CapabilitiesFailure { get; init; }
    public Exception? NotifyFailure { get; set; }
    public int CapabilityQueries { get; private set; }
    public event Action<uint, string>? ActionInvoked;
    public event Action<uint, uint>? NotificationClosed;

    public Task<uint> NotifyAsync(string summary, string body, IReadOnlyList<(string Key, string Label)> actions,
        bool critical = false, string? soundName = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (NotifyFailure is { } failure)
                return Task.FromException<uint>(failure);
            var id = ++_next;
            Notified.Add(new SentNotification(id, summary, body, actions, critical, soundName));
            Log.Add($"notify {id}");
            return Task.FromResult(id);
        }
    }

    public Task<bool> SupportsBodyMarkupAsync(CancellationToken ct = default)
    {
        CapabilityQueries++;
        return CapabilitiesFailure is null ? Task.FromResult(BodyMarkup) : Task.FromException<bool>(CapabilitiesFailure);
    }

    public Task CloseAsync(uint id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            Closed.Add(id);
            Log.Add($"close {id}");
        }
        return Task.CompletedTask;
    }

    public void RaiseAction(uint id, string key) => ActionInvoked?.Invoke(id, key);
    public void RaiseClosed(uint id, uint reason) => NotificationClosed?.Invoke(id, reason);
    public void Dispose() { }
}
