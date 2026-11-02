namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta que mantém um único temporizador armado para o próximo lembrete ou a virada do dia.</summary>
public interface IReminderScheduler : IDisposable
{
    /// <summary>Expira lembretes pendentes, sincroniza e arma o temporizador.</summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>Sincroniza os lembretes e rearma o temporizador.</summary>
    Task RescheduleAsync(CancellationToken ct = default);
}
