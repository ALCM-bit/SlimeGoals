namespace SlimeGoals.Platform.Linux.Notifications;

/// <summary>Costura fina sobre org.freedesktop.Notifications; o resto da lógica é testável sem D-Bus.</summary>
public interface INotificationBus : IDisposable
{
    /// <summary>
    /// Envia uma notificação e devolve o id atribuído pelo servidor. <paramref name="critical"/> manda a urgência crítica
    /// (o GNOME a mostra mesmo no "Não perturbe" e a mantém até ser dispensada); <paramref name="soundName"/> é um som do
    /// tema de sons (por exemplo "alarm-clock-elapsed"), tocado uma vez.
    /// </summary>
    Task<uint> NotifyAsync(string summary, string body, IReadOnlyList<(string Key, string Label)> actions,
        bool critical = false, string? soundName = null, CancellationToken ct = default);

    /// <summary>O servidor interpreta marcação no corpo (capacidade <c>body-markup</c>).</summary>
    Task<bool> SupportsBodyMarkupAsync(CancellationToken ct = default);

    /// <summary>Fecha a notificação (motivo 3 no sinal seguinte).</summary>
    Task CloseAsync(uint id, CancellationToken ct = default);

    /// <summary>Uma ação foi acionada: (id, chave).</summary>
    event Action<uint, string>? ActionInvoked;

    /// <summary>A notificação foi fechada: (id, motivo).</summary>
    event Action<uint, uint>? NotificationClosed;
}
