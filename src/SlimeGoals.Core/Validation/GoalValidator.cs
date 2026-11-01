using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Validation;

/// <summary>
/// Regras de validação de metas. Acumula todos os erros para a tela mostrar
/// cada mensagem junto do seu campo.
/// </summary>
public static class GoalValidator
{
    public const int MaxTitleLength = 80;
    public const int MaxTarget = 99;
    public const int MinReminderMinutes = 5;
    public const int MaxReminderMinutes = 24 * 60;

    public static IReadOnlyList<ValidationError> Validate(GoalDraft draft)
    {
        var errors = new List<ValidationError>();
        void Add(string field, string message) => errors.Add(new ValidationError(field, message));

        var title = draft.Title.Trim();
        if (title.Length == 0)
            Add(nameof(GoalDraft.Title), "Informe um título.");
        else if (title.Length > MaxTitleLength)
            Add(nameof(GoalDraft.Title), $"O título deve ter até {MaxTitleLength} caracteres.");

        if (draft.Target is < 1 or > MaxTarget)
            Add(nameof(GoalDraft.Target), $"O alvo deve ser entre 1 e {MaxTarget}.");

        if (draft.ReminderEveryMinutes is < MinReminderMinutes or > MaxReminderMinutes)
            Add(nameof(GoalDraft.ReminderEveryMinutes), "O intervalo deve ser entre 5 minutos e 24 horas.");

        if (draft.ActiveFrom >= draft.ActiveUntil)
            Add(nameof(GoalDraft.ActiveUntil), "O fim do horário deve ser depois do início.");

        if (draft.ActiveDays == WeekDays.None)
            Add(nameof(GoalDraft.ActiveDays), "Escolha ao menos um dia.");

        if (draft is { StartDate: { } start, EndDate: { } end } && end < start)
            Add(nameof(GoalDraft.EndDate), "A data de fim deve ser igual ou posterior à de início.");

        if (string.IsNullOrWhiteSpace(draft.Icon))
            Add(nameof(GoalDraft.Icon), "Escolha um ícone.");

        return errors;
    }
}
