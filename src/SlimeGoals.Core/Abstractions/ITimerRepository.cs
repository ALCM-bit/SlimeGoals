using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para o cronômetro único (tabela active_timer).</summary>
public interface ITimerRepository
{
    Task<ActiveTimer?> GetAsync(CancellationToken ct = default);

    /// <summary>Grava o cronômetro se não houver nenhum; devolve false se já existe um.</summary>
    Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default);

    /// <summary>Substitui o cronômetro existente (ex.: ao parar).</summary>
    Task SaveAsync(ActiveTimer timer, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);
}
