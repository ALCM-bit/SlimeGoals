using System.Globalization;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Goals;

/// <summary>Textos dos relatórios (Fase 5) em pt-BR.</summary>
public static class ReportFormatting
{
    private static readonly string[] ShortMonths =
        ["jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez"];

    private static readonly string[] FullMonths =
    [
        "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro",
    ];

    private static readonly string[] ShortDays = ["dom", "seg", "ter", "qua", "qui", "sex", "sáb"];

    /// <summary>"2 de nov" sem o "de": "2 nov".</summary>
    public static string DayMonth(DateOnly date) => $"{date.Day} {ShortMonths[date.Month - 1]}";

    /// <summary>"2 a 8 nov 2026", "26 out a 1 nov 2026", "29 dez 2025 a 4 jan 2026".</summary>
    public static string WeekTitle(DateRange week)
    {
        var (start, end) = (week.Start, week.EndExclusive.AddDays(-1));
        if (start.Year != end.Year)
            return $"{DayMonth(start)} {start.Year} a {DayMonth(end)} {end.Year}";
        if (start.Month != end.Month)
            return $"{DayMonth(start)} a {DayMonth(end)} {end.Year}";
        return $"{start.Day} a {DayMonth(end)} {end.Year}";
    }

    public static string MonthTitle(int year, int month) => $"{FullMonths[month - 1]} de {year}";

    public static string MonthSubtitle(DateRange range) =>
        $"Semanas de {DayMonth(range.Start)} a {DayMonth(range.EndExclusive.AddDays(-1))}";

    public static string Percent(int? percent) =>
        percent is { } p ? $"{p.ToString(CultureInfo.InvariantCulture)}%" : "—";

    public static string Count(int n, string singular, string plural) =>
        $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? singular : plural)}";

    public static string WeekDetail(WeekGoalRow row)
    {
        var goal = row.Goal;
        if (goal.Recurrence == Recurrence.Daily)
            return $"{row.MetPeriods} de {Count(row.CountedPeriods, "dia", "dias")}";
        return goal.Measure switch
        {
            Measure.Quantity => $"{row.Done} de {goal.Target} {GoalFormatting.UnitFor(goal.Target, goal.Unit)}",
            Measure.Duration => $"{GoalFormatting.Duration(row.Done)} de {GoalFormatting.Duration(goal.Target)}",
            Measure.Completion => row.Done >= goal.Target ? "Feito" : "Pendente",
            _ => throw new ArgumentOutOfRangeException(nameof(row), goal.Measure, null),
        };
    }

    public static string MonthDetail(MonthGoalRow row) => row.Goal.Recurrence == Recurrence.Weekly
        ? $"{row.MetPeriods} de {Count(row.CountedPeriods, "semana", "semanas")}"
        : $"{row.MetPeriods} de {Count(row.CountedPeriods, "dia", "dias")}";

    public static string Streak(MonthGoalRow row)
    {
        var weekly = row.Goal.Recurrence == Recurrence.Weekly;
        string Unit(int n) => weekly ? Count(n, "semana", "semanas") : Count(n, "dia", "dias");
        return $"Sequência: {Unit(row.Streak.Current)} · maior: {Unit(row.Streak.Best)}";
    }

    public static string? BestDay(WeekGoalRow row) =>
        row.BestDay is { } day ? $"Melhor dia: {ShortDays[(int)day.DayOfWeek]}" : null;

    public static string Reminders(int fired, int answered) =>
        $"{Count(fired, "disparado", "disparados")} · {Count(answered, "respondido", "respondidos")}";
}
