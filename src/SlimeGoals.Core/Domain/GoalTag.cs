namespace SlimeGoals.Core.Domain;

/// <summary>Ligação de uma tag a uma meta, com marca de remoção lógica.</summary>
public sealed record GoalTag
{
    public required Guid Id { get; init; }

    public required Guid GoalId { get; init; }

    /// <summary>Nome normalizado da tag (a ligação pode chegar antes da linha da tag).</summary>
    public required string TagName { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Quando a tag foi desmarcada da meta; null se a ligação vale.</summary>
    public DateTimeOffset? RemovedAt { get; init; }
}
