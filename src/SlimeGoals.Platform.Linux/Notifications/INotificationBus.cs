namespace SlimeGoals.Platform.Linux.Notifications;

/// <summary>Costura fina sobre org.freedesktop.Notifications; o resto da lógica é testável sem D-Bus.</summary>
public interface INotificationBus : IDisposable
{
    /// <summary>Envia uma notificação e devolve o id atribuído pelo servidor.</summary>
    Task<uint> NotifyAsync(string summary, string body, IReadOnlyList<(string Key, string Label)> actions,
        CancellationToken ct = default);

    /// <summary>Fecha a notificação (motivo 3 no sinal seguinte).</summary>
    Task CloseAsync(uint id, CancellationToken ct = default);

    /// <summary>Uma ação foi acionada: (id, chave).</summary>
    event Action<uint, string>? ActionInvoked;

    /// <summary>A notificação foi fechada: (id, motivo).</summary>
    event Action<uint, uint>? NotificationClosed;
}
