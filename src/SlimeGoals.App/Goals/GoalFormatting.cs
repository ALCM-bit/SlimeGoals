using System.Globalization;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Goals;

/// <summary>Textos de resumo de metas em pt-BR, usados na lista e no formulário.</summary>
public static class GoalFormatting
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Ordem ISO: segunda primeiro (semana do app, spec §5.1).
    private static readonly (WeekDays Day, string Short)[] DayNames =
    [
        (WeekDays.Monday, "Seg"), (WeekDays.Tuesday, "Ter"), (WeekDays.Wednesday, "Qua"), (WeekDays.Thursday, "Qui"),
        (WeekDays.Friday, "Sex"), (WeekDays.Saturday, "Sáb"), (WeekDays.Sunday, "Dom"),
    ];

    public static string Target(Recurrence recurrence, int target) => recurrence switch
    {
        Recurrence.Once => "Uma vez",
        Recurrence.Daily => $"{target}× por dia",
        Recurrence.Weekly => $"{target}× por semana",
        _ => throw new ArgumentOutOfRangeException(nameof(recurrence), recurrence, null),
    };

    public static string Interval(int minutes)
    {
        var (hours, rest) = (minutes / 60, minutes % 60);
        return (hours, rest) switch
        {
            (0, _) => $"{rest} min",
            (_, 0) => $"{hours} h",
            _ => $"{hours} h {rest} min",
        };
    }

    public static string Window(TimeOnly from, TimeOnly until) =>
        $"{from.ToString("HH:mm", Inv)}–{until.ToString("HH:mm", Inv)}";

    public static string Days(WeekDays days) => days switch
    {
        WeekDays.All => "Todos os dias",
        WeekDays.Weekdays => "Seg a sex",
        WeekDays.Saturday | WeekDays.Sunday => "Fim de semana",
        _ => string.Join(", ", DayNames.Where(d => days.HasFlag(d.Day)).Select(d => d.Short)),
    };

    public static string Period(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        (null, null) => "",
        ({ } s, null) => $"De {Short(s)}",
        (null, { } e) => $"Até {Short(e)}",
        ({ } s, { } e) => $"{Short(s)} a {Short(e)}",
    };

    public static string Summary(Goal goal)
    {
        var parts = new List<string>
        {
            Target(goal.Recurrence, goal.Target),
            $"a cada {Interval(goal.ReminderEveryMinutes)}",
            Window(goal.ActiveFrom, goal.ActiveUntil),
            Days(goal.ActiveDays),
        };
        var period = Period(goal.StartDate, goal.EndDate);
        if (period.Length > 0)
            parts.Add(period);
        return string.Join(" · ", parts);
    }

    private static string Short(DateOnly date) => date.ToString("dd/MM", Inv);
}
