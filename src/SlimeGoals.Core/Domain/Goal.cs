namespace SlimeGoals.Core.Domain;

/// <summary>
/// Meta persistida. Record imutável: alterações geram uma nova instância via <c>with</c>.
/// Instantes em UTC; datas e horários são locais (spec §8).
/// </summary>
public sealed record Goal
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required Recurrence Recurrence { get; init; }
    public required int Target { get; init; }
    public required string Icon { get; init; }
    public required GoalColor Color { get; init; }
    public required int ReminderEveryMinutes { get; init; }
    public required TimeOnly ActiveFrom { get; init; }
    public required TimeOnly ActiveUntil { get; init; }
    public required WeekDays ActiveDays { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }
}
