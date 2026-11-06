using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Services;

/// <summary>Progresso de uma meta no período atual. Done = soma (com sinal) dos registros do período.</summary>
public sealed record GoalProgress(Goal Goal, int Done, bool IsMet);

/// <summary>Resumo da semana do Hoje: % médio por meta, metas em andamento e concluídas hoje.</summary>
public sealed record WeekSummary(int Percent, int InProgress, int Completed);

/// <summary>
/// Instantâneo do Hoje. <see cref="Timer"/> é o cronômetro ativo mesmo que a meta dele não esteja na lista
/// (ex.: dia inativo), para a tela poder pará-lo; <see cref="TimerGoal"/> é a dona (null se não existe mais).
/// </summary>
public sealed record TodaySnapshot(IReadOnlyList<GoalProgress> Goals, WeekSummary Week, ActiveTimer? Timer, Goal? TimerGoal = null);

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
                var done = GoalTotals.Effective(g, SumIn(g, GoalPeriod.EvaluationPeriod(g, today)!.Value));
                return new GoalProgress(g, done, done >= g.Target);
            })
            .OrderBy(p => p.IsMet).ThenBy(p => p.Goal.CreatedAt)
            .ToList();

        var fractions = active
            .Select(g => WeekFraction(g, today, monday, zone, SumOn))
            .OfType<double>()
            .ToList();
        var percent = fractions.Count == 0 ? 0 : (int)Math.Round(fractions.Average() * 100, MidpointRounding.AwayFromZero);
        var met = list.Count(p => p.IsMet);

        var timer = await timers.GetAsync(ct);
        var timerGoal = timer is null ? null : await goals.GetIfReadableAsync(timer.GoalId, ct);

        return new TodaySnapshot(list, new WeekSummary(percent, list.Count - met, met), timer, timerGoal);
    }

    /// <summary>
    /// Fração da semana cumprida pela meta (spec §7), ou null se ela não entra na média.
    /// Semanal: soma da semana ÷ alvo. Diária: média, nos dias ativos de segunda a hoje, da fração cumprida
    /// em cada dia (até 1). Hoje entra com o progresso parcial, para o % subir à medida que se registra.
    /// Dias antes da criação da meta (data local) não contam: ela nem existia neles.
    /// </summary>
    private static double? WeekFraction(Goal goal, DateOnly today, DateOnly monday, TimeZoneInfo zone,
        Func<Goal, DateOnly, int> sumOn)
    {
        var createdOn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(goal.CreatedAt, zone).DateTime);
        var days = Enumerable.Range(0, today.DayNumber - monday.DayNumber + 1).Select(monday.AddDays).ToList();
        if (goal.Recurrence == Recurrence.Weekly)
        {
            if (!days.Any(d => GoalPeriod.IsInPeriod(goal, d)))
                return null;
            var sum = Enumerable.Range(0, 7).Sum(i => sumOn(goal, monday.AddDays(i)));
            return Math.Clamp((double)sum / goal.Target, 0, 1);
        }

        var counted = days.Where(d => d >= createdOn && GoalPeriod.IsShownOn(goal, d)).ToList();
        return counted.Count == 0 ? null : counted.Average(d => Math.Clamp((double)sumOn(goal, d) / goal.Target, 0, 1));
    }
}
