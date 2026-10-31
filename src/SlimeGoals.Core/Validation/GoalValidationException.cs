namespace SlimeGoals.Core.Validation;

public sealed class GoalValidationException(IReadOnlyList<ValidationError> errors)
    : Exception("A meta tem campos inválidos: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
{
    public IReadOnlyList<ValidationError> Errors { get; } = errors;
}
