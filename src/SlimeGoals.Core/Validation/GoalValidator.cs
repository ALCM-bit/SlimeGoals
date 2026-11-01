using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Validation;

/// <summary>
/// Regras de validação de metas. Acumula todos os erros para a tela mostrar
/// cada mensagem junto do seu campo.
/// </summary>
public static class GoalValidator
{
    public const int MaxTitleLength = 80;
    public const int MaxQuantityTarget = 9999;
    public const int MaxDailyMinutes = 24 * 60;
    public const int MaxWeeklyMinutes = 7 * 24 * 60;
    public const int MaxUnitLength = 20;
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

        ValidateTarget(draft, Add);

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

    private static void ValidateTarget(GoalDraft draft, Action<string, string> add)
    {
        const string target = nameof(GoalDraft.Target);
        switch (draft.Measure)
        {
            case Measure.Completion:
                if (draft.Target != 1)
                    add(target, "Meta de conclusão tem alvo 1.");
                break;
            case Measure.Quantity:
                if (draft.Target is < 1 or > MaxQuantityTarget)
                    add(target, $"O alvo deve ser entre 1 e {MaxQuantityTarget}.");
                var unit = draft.Unit?.Trim() ?? "";
                if (unit.Length == 0)
                    add(nameof(GoalDraft.Unit), "Informe a unidade.");
                else if (unit.Length > MaxUnitLength)
                    add(nameof(GoalDraft.Unit), $"A unidade deve ter até {MaxUnitLength} caracteres.");
                break;
            case Measure.Duration:
                var (max, label) = draft.Recurrence == Recurrence.Daily ? (MaxDailyMinutes, "24 h") : (MaxWeeklyMinutes, "168 h");
                if (draft.Target < 1 || draft.Target > max)
                    add(target, $"O tempo deve ser entre 1 min e {label}.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(draft), draft.Measure, "Medida desconhecida.");
        }
    }
}
