namespace SlimeGoals.Core.Domain;

/// <summary>As tags que já existem em todo aparelho, sem passar pela sincronização.</summary>
public static class DefaultTags
{
    /// <summary>Data das tags padrão: a mais antiga possível, para qualquer edição vencer.</summary>
    public static readonly DateTimeOffset Seed = DateTimeOffset.UnixEpoch;

    public static IReadOnlyList<Tag> All { get; } =
    [
        Make("medicamentos", GoalColor.Green),
        Make("estudos", GoalColor.Purple),
        Make("tarefas", GoalColor.Amber),
    ];

    private static Tag Make(string name, GoalColor color) => new()
    {
        Id = TagIds.ForTag(name),
        Name = name,
        Color = color,
        UpdatedAt = Seed,
    };
}
