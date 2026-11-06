namespace SlimeGoals.Core.Domain;

/// <summary>
/// Um período contado (dia da diária, semana da semanal; semana = segunda a segunda).
/// InProgress = é o período de hoje, ainda em andamento.
/// </summary>
public sealed record PeriodResult(DateRange Range, int Done, bool Met, bool InProgress = false);

/// <summary>
/// Histórico de uma meta para os relatórios: quando ela existiu e quais períodos contam.
/// Um período só conta se a meta existia em algum dia dele (criação ≤ dia ≤ arquivamento, datas locais).
/// </summary>
public static class GoalHistory
{
    public static DateOnly CreatedOn(Goal goal, TimeZoneInfo zone) => LocalDay(goal.CreatedAt, zone);

    public static DateOnly? ArchivedOn(Goal goal, TimeZoneInfo zone) =>
        goal.ArchivedAt is { } archivedAt ? LocalDay(archivedAt, zone) : null;

    /// <summary>A meta existia no dia: criada, não arquivada e dentro do período (sem o filtro de dia ativo).</summary>
    public static bool ExistedOn(Goal goal, DateOnly day, TimeZoneInfo zone) =>
        day >= CreatedOn(goal, zone) &&
        (ArchivedOn(goal, zone) is not { } archivedOn || day <= archivedOn) &&
        GoalPeriod.IsInPeriod(goal, day);

    /// <summary>
    /// Períodos contados que intersectam o intervalo, em ordem e sem dias depois de hoje.
    /// O período que contém hoje entra como pendente até ser cumprido (InProgress); a sequência o ignora
    /// enquanto não for cumprido (<see cref="Streaks.From"/>).
    /// </summary>
    public static IReadOnlyList<PeriodResult> Periods(Goal goal, DateRange range, DateOnly today,
        TimeZoneInfo zone, DailySums sums)
    {
        var result = new List<PeriodResult>();

        if (goal.Recurrence == Recurrence.Daily)
        {
            for (var d = range.Start; d < range.EndExclusive && d <= today; d = d.AddDays(1))
            {
                if (!ExistedOn(goal, d, zone) || !goal.ActiveDays.Includes(d.DayOfWeek))
                    continue;
                var done = GoalTotals.Effective(goal, sums.On(goal.Id, d));
                result.Add(new PeriodResult(new DateRange(d, d.AddDays(1)), done, done >= goal.Target, d == today));
            }
            return result;
        }

        for (var monday = WeekMath.StartOfWeek(range.Start); monday < range.EndExclusive; monday = monday.AddDays(7))
        {
            var week = new DateRange(monday, monday.AddDays(7));
            DateOnly? first = null;
            for (var d = monday; d < week.EndExclusive && d <= today; d = d.AddDays(1))
            {
                if (ExistedOn(goal, d, zone))
                {
                    first = d;
                    break;
                }
            }
            if (first is not { } day)
                continue;

            var done = GoalTotals.Effective(goal, sums.In(goal.Id, GoalPeriod.EvaluationPeriod(goal, day)!.Value));
            result.Add(new PeriodResult(week, done, done >= goal.Target, week.Contains(today)));
        }
        return result;
    }

    private static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
