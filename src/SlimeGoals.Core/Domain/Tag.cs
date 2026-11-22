namespace SlimeGoals.Core.Domain;

/// <summary>Uma tag: nome normalizado único, cor e marca de exclusão lógica.</summary>
public sealed record Tag
{
    public required Guid Id { get; init; }

    /// <summary>Nome já normalizado (<see cref="TagName.Normalize"/>).</summary>
    public required string Name { get; init; }

    public required GoalColor Color { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Quando a tag foi excluída; null se ela existe.</summary>
    public DateTimeOffset? DeletedAt { get; init; }
}
