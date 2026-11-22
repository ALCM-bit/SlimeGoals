namespace SlimeGoals.Core.Domain;

/// <summary>Linhas de tag e de ligação a gravar juntas, na mesma transação.</summary>
public sealed record TagChanges(IReadOnlyList<Tag> Tags, IReadOnlyList<GoalTag> Links)
{
    public static TagChanges Empty { get; } = new([], []);

    public bool IsEmpty => Tags.Count == 0 && Links.Count == 0;
}
