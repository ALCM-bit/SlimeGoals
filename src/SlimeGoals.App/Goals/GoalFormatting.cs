using System.Globalization;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

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

    /// <summary>Alvo do período conforme a medida: "8 copos por dia", "5 h por semana", "Concluir na semana".</summary>
    public static string Target(Goal goal) => goal.Measure switch
    {
        Measure.Completion => goal.Recurrence == Recurrence.Weekly ? "Concluir na semana" : "Concluir no dia",
        Measure.Quantity => $"{goal.Target} {UnitFor(goal.Target, goal.Unit)} {PerPeriod(goal.Recurrence)}",
        Measure.Duration => $"{Duration(goal.Target)} {PerPeriod(goal.Recurrence)}",
        _ => throw new ArgumentOutOfRangeException(nameof(goal), goal.Measure, null),
    };

    /// <summary>Progresso do período atual: "5 de 8 copos hoje", "2 h 30 de 5 h na semana", "Feito hoje", "Pendente".</summary>
    public static string Progress(GoalProgress progress)
    {
        var goal = progress.Goal;
        var when = goal.Recurrence == Recurrence.Weekly ? "na semana" : "hoje";
        return goal.Measure switch
        {
            Measure.Completion => progress.IsMet ? $"Feito {when}" : "Pendente",
            Measure.Quantity => $"{progress.Done} de {goal.Target} {UnitFor(goal.Target, goal.Unit)} {when}",
            Measure.Duration => $"{Duration(progress.Done)} de {Duration(goal.Target)} {when}",
            _ => throw new ArgumentOutOfRangeException(nameof(progress), goal.Measure, null),
        };
    }

    /// <summary>Tempo de meta: 45 → "45 min"; 120 → "2 h"; 150 → "2 h 30".</summary>
    public static string Duration(int minutes)
    {
        var (hours, rest) = (Math.Abs(minutes) / 60, Math.Abs(minutes) % 60);
        var sign = minutes < 0 ? "-" : "";
        return (hours, rest) switch
        {
            (0, _) => $"{sign}{rest} min",
            (_, 0) => $"{sign}{hours} h",
            _ => $"{sign}{hours} h {rest:00}",
        };
    }

    /// <summary>Cronômetro: "0:05:09", "1:02:03" (horas sem zero à esquerda).</summary>
    public static string Elapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";

    /// <summary>
    /// Singular só para a unidade padrão: 1 → "vez". A unidade é texto livre ("copos", "páginas"),
    /// e flexionar texto livre em pt-BR exigiria regras que erram mais do que acertam.
    /// </summary>
    private static string? UnitFor(int amount, string? unit) => (amount, unit) switch
    {
        (1, "vezes") => "vez",
        (1, "Vezes") => "Vez",
        _ => unit,
    };

    private static string PerPeriod(Recurrence recurrence) => recurrence == Recurrence.Weekly ? "por semana" : "por dia";

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
            Target(goal),
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
