namespace SlimeGoals.Core.Domain;

/// <summary>Dados editáveis de uma meta, como vêm do formulário (sem id nem carimbos de tempo).</summary>
public sealed record GoalDraft(
    string Title,
    Recurrence Recurrence,
    int Target,
    string Icon,
    GoalColor Color,
    int ReminderEveryMinutes,
    TimeOnly ActiveFrom,
    TimeOnly ActiveUntil,
    WeekDays ActiveDays,
    DateOnly? StartDate,
    DateOnly? EndDate);
