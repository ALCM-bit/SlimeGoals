namespace SlimeGoals.Core;

public sealed class GoalNotFoundException(Guid id) : Exception($"Meta {id} não encontrada.")
{
    public Guid GoalId { get; } = id;
}
