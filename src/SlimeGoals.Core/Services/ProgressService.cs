using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>Progresso de uma meta no período atual. Done = soma (com sinal) dos registros do período.</summary>
public sealed record GoalProgress(Goal Goal, int Done, bool IsMet);

/// <summary>Resumo da semana do Hoje: % médio por meta, metas em andamento e concluídas hoje.</summary>
public sealed record WeekSummary(int Percent, int InProgress, int Completed);

public sealed record TodaySnapshot(IReadOnlyList<GoalProgress> Goals, WeekSummary Week, ActiveTimer? Timer);

/// <summary>
/// Monta o instantâneo da tela Hoje (spec §7) com uma consulta de registros só: a semana local atual.
/// Todo período de avaliação de hoje cabe nessa semana (diária = hoje; semanal = semana recortada).
/// </summary>
public sealed class ProgressService(IGoalRepository goals, ICheckinRepository checkins, ITimerRepository timers, TimeProvider time)
{
    public async Task<TodaySnapshot> GetTodayAsync(CancellationToken ct = default)
    {
        var zone = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var monday = WeekMath.StartOfWeek(today);
        var (from, to) = new DateRange(monday, monday.AddDays(7)).ToUtc(zone);

        var active = await goals.ListAsync(includeArchived: false, ct);
        var byGoalAndDay = (await checkins.ListAsync(from, to, ct))
            .GroupBy(c => (c.GoalId, Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.DoneAt, zone).DateTime)))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        int SumOn(Goal goal, DateOnly day) => byGoalAndDay.GetValueOrDefault((goal.Id, day));
        int SumIn(Goal goal, DateRange range) =>
            byGoalAndDay.Where(kv => kv.Key.GoalId == goal.Id && range.Contains(kv.Key.Day)).Sum(kv => kv.Value);

        var list = active
            .Where(g => GoalPeriod.IsShownOn(g, today))
            .Select(g =>
            {
                var done = SumIn(g, GoalPeriod.EvaluationPeriod(g, today)!.Value);
                return new GoalProgress(g, done, done >= g.Target);
            })
            .OrderBy(p => p.IsMet).ThenBy(p => p.Goal.CreatedAt)
            .ToList();

        var fractions = active
            .Select(g => WeekFraction(g, today, monday, SumOn))
            .OfType<double>()
            .ToList();
        var percent = fractions.Count == 0 ? 0 : (int)Math.Round(fractions.Average() * 100, MidpointRounding.AwayFromZero);
        var met = list.Count(p => p.IsMet);

        var timer = await timers.GetAsync(ct);
        if (timer is not null && list.All(p => p.Goal.Id != timer.GoalId))
            timer = null;

        return new TodaySnapshot(list, new WeekSummary(percent, list.Count - met, met), timer);
    }

    /// <summary>
    /// Fração da semana cumprida pela meta (spec §7), ou null se ela não entra na média.
    /// Semanal: soma da semana ÷ alvo. Diária: dias ativos cumpridos ÷ dias ativos de segunda a ontem,
    /// mais hoje só se já cumprido (o dia em andamento não puxa a média para baixo).
    /// </summary>
    private static double? WeekFraction(Goal goal, DateOnly today, DateOnly monday, Func<Goal, DateOnly, int> sumOn)
    {
        var days = Enumerable.Range(0, today.DayNumber - monday.DayNumber + 1).Select(monday.AddDays).ToList();
        if (goal.Recurrence == Recurrence.Weekly)
        {
            if (!days.Any(d => GoalPeriod.IsInPeriod(goal, d)))
                return null;
            var sum = Enumerable.Range(0, 7).Sum(i => sumOn(goal, monday.AddDays(i)));
            return Math.Clamp((double)sum / goal.Target, 0, 1);
        }

        bool Met(DateOnly d) => sumOn(goal, d) >= goal.Target;
        var counted = days.Where(d => GoalPeriod.IsShownOn(goal, d) && (d < today || Met(d))).ToList();
        return counted.Count == 0 ? null : (double)counted.Count(Met) / counted.Count;
    }
}
