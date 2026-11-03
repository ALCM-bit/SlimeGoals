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

/// <summary>Barra de uma semana do mês. Percent null = nenhuma meta com valor na semana.</summary>
public sealed record WeekBar(DateOnly Monday, int? Percent);

/// <summary>
/// Linha de uma meta no mês. WeekPercents: um valor por semana de Range (null = meta não existiu naquela semana
/// ou semana futura).
/// </summary>
public sealed record MonthGoalRow(Goal Goal, int MetPeriods, int CountedPeriods, int? Percent,
    IReadOnlyList<int?> WeekPercents, Streak Streak);

/// <summary>Relatório de um mês, em semanas inteiras (segunda a domingo) cuja segunda cai no mês.</summary>
public sealed record MonthReport(int Year, int Month, DateRange Range, IReadOnlyList<WeekBar> Weeks,
    IReadOnlyList<MonthGoalRow> Goals, int? Percent);

/// <summary>Destaque do Hoje: Length na unidade do tipo da meta (dias ou semanas).</summary>
public sealed record StreakHighlight(Goal Goal, int Length);

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

    /// <summary>Da primeira segunda do mês até a segunda seguinte à última segunda do mês.</summary>
    public static DateRange MonthRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.DayOfWeek == DayOfWeek.Monday ? first : WeekMath.StartOfWeek(first).AddDays(7);
        var nextMonth = first.AddMonths(1);
        var end = nextMonth.DayOfWeek == DayOfWeek.Monday ? nextMonth : WeekMath.StartOfWeek(nextMonth).AddDays(7);
        return new DateRange(start, end);
    }

    public async Task<MonthReport> GetMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var range = MonthRange(year, month);
        var mondays = Enumerable.Range(0, (range.EndExclusive.DayNumber - range.Start.DayNumber) / 7)
            .Select(i => range.Start.AddDays(7 * i)).ToList();

        var listed = (await goals.ListAsync(includeArchived: true, ct))
            .Where(g => FirstExistingDay(g, range, today, zone) is not null)
            .OrderBy(g => g.CreatedAt)
            .ToList();

        var lower = listed.Select(g => GoalHistory.CreatedOn(g, zone)).Append(range.Start).Min();
        var (from, to) = new DateRange(lower, range.EndExclusive).ToUtc(zone);
        var sums = new DailySums(await checkins.ListAsync(from, to, ct), zone);

        var cutoff = Min(range.EndExclusive.AddDays(-1), today);
        var rows = listed.Select(g => MonthRow(g, range, mondays, cutoff, today, zone, sums)).ToList();

        var weeks = mondays.Select((monday, i) =>
        {
            var values = rows.Select(r => r.WeekPercents[i]).Where(v => v is not null).Select(v => v!.Value).ToList();
            return new WeekBar(monday, values.Count == 0 ? null : Round(values.Average()));
        }).ToList();

        var percents = rows.Where(r => r.Percent is not null).Select(r => r.Percent!.Value).ToList();
        int? overall = percents.Count == 0 ? null : Round(percents.Average());
        return new MonthReport(year, month, range, weeks, rows, overall);
    }

    /// <summary>Meta ativa com a maior sequência atual (semana vale 7 dias); null se nenhuma tem sequência.</summary>
    public async Task<StreakHighlight?> GetTopStreakAsync(CancellationToken ct = default)
    {
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var active = (await goals.ListAsync(includeArchived: false, ct)).OrderBy(g => g.CreatedAt).ToList();
        if (active.Count == 0)
            return null;

        var lower = active.Select(g => GoalHistory.CreatedOn(g, zone)).Min();
        var (from, to) = new DateRange(lower, today.AddDays(1)).ToUtc(zone);
        var sums = new DailySums(await checkins.ListAsync(from, to, ct), zone);

        StreakHighlight? top = null;
        var topDays = 0;
        foreach (var goal in active)
        {
            var current = StreakOf(goal, today, today, zone, sums).Current;
            var days = current * (goal.Recurrence == Recurrence.Weekly ? 7 : 1);
            if (days > topDays)
            {
                top = new StreakHighlight(goal, current);
                topDays = days;
            }
        }
        return top;
    }

    private static MonthGoalRow MonthRow(Goal goal, DateRange range, IReadOnlyList<DateOnly> mondays, DateOnly cutoff,
        DateOnly today, TimeZoneInfo zone, DailySums sums)
    {
        var periods = GoalHistory.Periods(goal, range, today, zone, sums);
        var met = periods.Count(p => p.Met);
        int? percent = periods.Count == 0 ? null : Round(met * 100.0 / periods.Count);

        var weekPercents = mondays.Select(monday =>
        {
            var week = new DateRange(monday, monday.AddDays(7));
            if (goal.Recurrence == Recurrence.Daily)
            {
                var days = GoalHistory.Periods(goal, week, today, zone, sums);
                return days.Count == 0 ? (int?)null : Round(days.Count(p => p.Met) * 100.0 / days.Count);
            }
            if (FirstExistingDay(goal, week, today, zone) is not { } first)
                return null;
            var done = sums.In(goal.Id, GoalPeriod.EvaluationPeriod(goal, first)!.Value);
            return Round(Math.Min(1.0, (double)done / goal.Target) * 100);
        }).ToList();

        return new MonthGoalRow(goal, met, periods.Count, percent, weekPercents, StreakOf(goal, cutoff, today, zone, sums));
    }

    private static Streak StreakOf(Goal goal, DateOnly cutoff, DateOnly today, TimeZoneInfo zone, DailySums sums) =>
        Streaks.From(GoalHistory.Periods(goal,
            new DateRange(GoalHistory.CreatedOn(goal, zone), cutoff.AddDays(1)), today, zone, sums));

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

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
