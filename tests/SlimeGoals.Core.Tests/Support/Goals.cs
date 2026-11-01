using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Support;

/// <summary>Construtores de dados de teste.</summary>
internal static class Goals
{
    public static DateOnly D(string iso) => DateOnly.Parse(iso);

    public static Goal G(Recurrence r, string? start = null, string? end = null, WeekDays days = WeekDays.All) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Meta de teste",
        Recurrence = r,
        Target = 3,
        Icon = "book",
        Color = GoalColor.Purple,
        ReminderEveryMinutes = 120,
        ActiveFrom = new TimeOnly(9, 0),
        ActiveUntil = new TimeOnly(18, 0),
        ActiveDays = days,
        StartDate = start is null ? null : D(start),
        EndDate = end is null ? null : D(end),
        CreatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
    };
}
