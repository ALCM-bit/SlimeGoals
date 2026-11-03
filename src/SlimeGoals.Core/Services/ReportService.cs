using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>Barra de um dia: quantos registros positivos houve (ajustes negativos são correções e não contam).</summary>
public sealed record DayBar(DateOnly Day, int Records);

/// <summary>
/// Linha de uma meta na semana. Done: soma da semana (semanal: período de avaliação recortado).
/// Percent null = sem período contado. BestDay só para meta semanal.
/// </summary>
public sealed record WeekGoalRow(Goal Goal, int Done, int MetPeriods, int CountedPeriods, int? Percent, DateOnly? BestDay);

/// <summary>Relatório de uma semana (segunda a domingo). Percent = média dos % das metas que têm valor.</summary>
public sealed record WeekReport(DateRange Week, IReadOnlyList<DayBar> Days, IReadOnlyList<WeekGoalRow> Goals,
    int? Percent, int RemindersFired, int RemindersAnswered);

/// <summary>Monta os relatórios (Fase 5) a partir do histórico, com uma consulta de registros por período.</summary>
public sealed class ReportService(IGoalRepository goals, ICheckinRepository checkins, IReminderLogRepository log,
    TimeProvider time)
{
    /// <summary>Semana (segunda a domingo) que contém <paramref name="day"/>.</summary>
    public async Task<WeekReport> GetWeekAsync(DateOnly day, CancellationToken ct = default)
    {
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var monday = WeekMath.StartOfWeek(day);
        var week = new DateRange(monday, monday.AddDays(7));
        var (from, to) = week.ToUtc(zone);

        var sums = new DailySums(await checkins.ListAsync(from, to, ct), zone);

        var rows = (await goals.ListAsync(includeArchived: true, ct))
            .Where(g => FirstExistingDay(g, week, today, zone) is not null)
            .OrderBy(g => g.CreatedAt)
            .Select(g => Row(g, week, today, zone, sums))
            .ToList();

        var ids = rows.Select(r => r.Goal.Id).ToHashSet();
        var days = Enumerable.Range(0, 7).Select(i => monday.AddDays(i))
            .Select(d => new DayBar(d, sums.PositiveCountOn(d, ids))).ToList();

        var reminders = (await log.ListAsync(from, to, ct)).Where(e => ids.Contains(e.GoalId)).ToList();
        var answered = reminders.Count(e => e.Response is ReminderResponse.Done or ReminderResponse.Snooze);

        var percents = rows.Where(r => r.Percent is not null).Select(r => r.Percent!.Value).ToList();
        int? overall = percents.Count == 0 ? null : Round(percents.Average());

        return new WeekReport(week, days, rows, overall, reminders.Count, answered);
    }

    private static WeekGoalRow Row(Goal goal, DateRange week, DateOnly today, TimeZoneInfo zone, DailySums sums)
    {
        var periods = GoalHistory.Periods(goal, week, today, zone, sums);
        var met = periods.Count(p => p.Met);

        if (goal.Recurrence == Recurrence.Daily)
        {
            var done = periods.Sum(p => p.Done);
            int? percent = periods.Count == 0 ? null : Round(met * 100.0 / periods.Count);
            return new WeekGoalRow(goal, done, met, periods.Count, percent, null);
        }

        var first = FirstExistingDay(goal, week, today, zone)!.Value;
        var evaluation = GoalPeriod.EvaluationPeriod(goal, first)!.Value;
        var weekDone = sums.In(goal.Id, evaluation);
        var weeklyPercent = Round(Math.Min(1.0, (double)weekDone / goal.Target) * 100);

        DateOnly? best = null;
        var bestSum = 0;
        for (var d = evaluation.Start; d < evaluation.EndExclusive && d <= today; d = d.AddDays(1))
        {
            var sum = sums.On(goal.Id, d);
            if (sum > bestSum)
            {
                best = d;
                bestSum = sum;
            }
        }
        return new WeekGoalRow(goal, weekDone, met, periods.Count, weeklyPercent, best);
    }

    private static DateOnly? FirstExistingDay(Goal goal, DateRange week, DateOnly today, TimeZoneInfo zone)
    {
        for (var d = week.Start; d < week.EndExclusive && d <= today; d = d.AddDays(1))
        {
            if (GoalHistory.ExistedOn(goal, d, zone))
                return d;
        }
        return null;
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
