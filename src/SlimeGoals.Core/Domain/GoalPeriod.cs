namespace SlimeGoals.Core.Domain;

/// <summary>
/// Regras de calendário de uma meta e o seu período de avaliação (spec §5.1).
/// É a única fonte da pergunta "qual intervalo conta para o progresso?",
/// usada pela tela Hoje, pelo planejador de lembretes e pelos relatórios.
/// Arquivamento e exclusão não entram aqui: são filtros dos serviços.
/// </summary>
public static class GoalPeriod
{
    /// <summary>A data está entre StartDate e EndDate (inclusivos; lados nulos = aberto).</summary>
    public static bool IsInPeriod(Goal goal, DateOnly date) =>
        (goal.StartDate is not { } start || date >= start) &&
        (goal.EndDate is not { } end || date <= end);

    /// <summary>Aparece na tela Hoje: diária só nos dias ativos; semanal e única em qualquer dia do período.</summary>
    public static bool IsShownOn(Goal goal, DateOnly date) =>
        IsInPeriod(goal, date) &&
        (goal.Recurrence != Recurrence.Daily || goal.ActiveDays.Includes(date.DayOfWeek));

    /// <summary>Gera lembretes na data: dentro do período e em dia ativo, para os três tipos.</summary>
    public static bool RemindsOn(Goal goal, DateOnly date) =>
        IsInPeriod(goal, date) && goal.ActiveDays.Includes(date.DayOfWeek);

    /// <summary>Intervalo [início, fim) que conta para o progresso na data, ou null fora do período.</summary>
    public static DateRange? EvaluationPeriod(Goal goal, DateOnly date)
    {
        if (!IsInPeriod(goal, date))
            return null;

        var periodStart = goal.StartDate ?? DateOnly.MinValue;
        var periodEnd = goal.EndDate is { } end ? NextDay(end) : DateOnly.MaxValue;

        return goal.Recurrence switch
        {
            Recurrence.Once => new DateRange(periodStart, periodEnd),
            Recurrence.Daily => new DateRange(date, NextDay(date)),
            Recurrence.Weekly => Clip(new DateRange(WeekMath.StartOfWeek(date), WeekMath.StartOfWeek(date).AddDays(7)),
                                      periodStart, periodEnd),
            _ => throw new ArgumentOutOfRangeException(nameof(goal), goal.Recurrence, "Recorrência desconhecida."),
        };
    }

    // MaxValue é o sentinela de "aberto": somar um dia estouraria.
    private static DateOnly NextDay(DateOnly date) => date == DateOnly.MaxValue ? date : date.AddDays(1);

    private static DateRange Clip(DateRange range, DateOnly start, DateOnly endExclusive) =>
        new(range.Start > start ? range.Start : start,
            range.EndExclusive < endExclusive ? range.EndExclusive : endExclusive);
}
