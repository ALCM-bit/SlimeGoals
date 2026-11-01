namespace SlimeGoals.Core;

/// <summary>
/// Operação impossível no estado atual da meta (ex.: editar uma arquivada).
/// A mensagem está em pt-BR, pronta para a tela.
/// </summary>
public sealed class GoalStateException(Guid goalId, string message) : Exception(message)
{
    public Guid GoalId { get; } = goalId;
}
