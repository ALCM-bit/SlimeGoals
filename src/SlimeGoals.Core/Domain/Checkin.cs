namespace SlimeGoals.Core.Domain;

/// <summary>
/// Um ajuste no progresso de uma meta (spec §5.2): valor com sinal na unidade base da medida
/// (1/−1 na conclusão, podendo ser maior por causa da compensação do total limitado; unidades na quantidade, minutos no tempo). O progresso é a soma do período.
/// </summary>
public sealed record Checkin
{
    public required Guid Id { get; init; }
    public required Guid GoalId { get; init; }
    public required DateTimeOffset DoneAt { get; init; }
    public required int Amount { get; init; }
    public string? Note { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
