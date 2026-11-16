using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para o cronômetro único (tabela active_timer).</summary>
public interface ITimerRepository
{
    Task<ActiveTimer?> GetAsync(CancellationToken ct = default);

    /// <summary>Grava o cronômetro se não houver nenhum; devolve false se já existe um.</summary>
    Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default);

    /// <summary>Substitui o cronômetro existente.</summary>
    Task SaveAsync(ActiveTimer timer, CancellationToken ct = default);

    /// <summary>
    /// Para o cronômetro rodando em <paramref name="at"/>, sem tocar no início nem no nome (uma troca de tarefa de outro
    /// processo pode tê-los mudado depois de quem para ler). Falso se não há cronômetro rodando.
    /// </summary>
    Task<bool> StopAsync(DateTimeOffset at, CancellationToken ct = default);

    Task ClearAsync(CancellationToken ct = default);

    /// <summary>Troca só o nome da tarefa do cronômetro existente. Falso quando não há cronômetro (nunca cria um).</summary>
    Task<bool> SetTaskAsync(string? task, CancellationToken ct = default);

    /// <summary>
    /// Numa transação só: grava o trecho <paramref name="chunk"/> (com a fila da sincronização; sem trecho, só o nome muda)
    /// e passa o cronômetro para <paramref name="next"/>, mas só se ele ainda for o da mesma meta, rodando, com início
    /// <paramref name="expectedStartedAt"/>. Falso, sem gravar nada, se o cronômetro mudou.
    /// </summary>
    Task<bool> SwitchAsync(DateTimeOffset expectedStartedAt, ActiveTimer next, Checkin? chunk, CancellationToken ct = default);
}
