namespace SlimeGoals.Platform.Linux.Power;

/// <summary>Sinal PrepareForSleep do logind: true ao entrar em suspensão, false ao acordar.</summary>
public interface ISleepSignalSource : IAsyncDisposable
{
    Task StartAsync(Action<bool> onPrepareForSleep, CancellationToken ct = default);
}
