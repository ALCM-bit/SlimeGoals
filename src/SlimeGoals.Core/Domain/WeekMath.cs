namespace SlimeGoals.Core.Domain;

/// <summary>Cálculos de semana. A semana do app é fixa: segunda a domingo (spec §5.1).</summary>
public static class WeekMath
{
    public static DateOnly StartOfWeek(DateOnly date)
    {
        // DayOfWeek: domingo = 0. Dias desde a segunda: seg = 0 … dom = 6.
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
