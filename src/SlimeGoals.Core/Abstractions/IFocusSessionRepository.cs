using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Abstractions;

/// <summary>Porta de saída para a sessão única de Pomodoro e os seus trechos (tabelas focus_session e focus_chunk).</summary>
public interface IFocusSessionRepository
{
    Task<FocusSession?> GetAsync(CancellationToken ct = default);

    /// <summary>Trechos da sessão, em ordem de <see cref="FocusChunk.EndedAt"/>.</summary>
    Task<IReadOnlyList<FocusChunk>> ChunksAsync(CancellationToken ct = default);

    /// <summary>Grava a sessão se não houver sessão nem cronômetro, num passo só; falso nos outros casos.</summary>
    Task<bool> TryStartAsync(FocusSession session, CancellationToken ct = default);

    /// <summary>
    /// Numa transação só: grava o registro <paramref name="checkin"/> (com a fila da sincronização), o trecho
    /// <paramref name="chunk"/> e a sessão <paramref name="next"/> com a revisão <c>expectedRevision + 1</c> (a
    /// <see cref="FocusSession.Revision"/> de <paramref name="next"/> não é usada; no mesmo passo, a
    /// <see cref="FocusSession.AlertedStep"/> gravada fica, para o aviso não tocar de novo). <paramref name="next"/> nulo apaga a
    /// sessão e os trechos. Falso, sem gravar nada, se a revisão gravada não é <paramref name="expectedRevision"/>.
    /// </summary>
    Task<bool> UpdateAsync(int expectedRevision, FocusSession? next, FocusChunk? chunk, Checkin? checkin,
        CancellationToken ct = default);

    /// <summary>
    /// Numa transação só: grava o registro e o liga ao trecho, se o trecho ainda está sem registro. Falso, sem gravar,
    /// se o trecho sumiu ou já tem registro.
    /// </summary>
    Task<bool> AssignChunkAsync(Guid chunkId, Checkin checkin, CancellationToken ct = default);

    /// <summary>Marca o aviso do bloco <paramref name="step"/> como dado, só se ele ainda é o atual. Não muda a revisão.</summary>
    Task<bool> MarkAlertedAsync(int step, CancellationToken ct = default);

    /// <summary>Apaga a sessão e os trechos.</summary>
    Task ClearAsync(CancellationToken ct = default);
}
