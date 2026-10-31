using System.Globalization;

namespace SlimeGoals.Core.Domain;

/// <summary>
/// Conjunto de dias da semana como bit flags. Um enum [Flags] tem igualdade por valor,
/// o que mantém o record <see cref="Goal"/> comparável sem coleções.
/// </summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Weekdays | Saturday | Sunday,
}

public static class WeekDaysExtensions
{
    /// <summary>Converte <see cref="DayOfWeek"/> (domingo = 0) para o número ISO (segunda = 1 … domingo = 7).</summary>
    private static int IsoNumber(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static WeekDays FromIsoNumber(int iso) => (WeekDays)(1 << (iso - 1));

    public static bool Includes(this WeekDays days, DayOfWeek day) =>
        (days & FromIsoNumber(IsoNumber(day))) != 0;

    /// <summary>Formato do banco: "1,2,3" em ordem crescente, 1 = segunda (spec §8).</summary>
    public static string ToStorage(this WeekDays days) =>
        string.Join(',', Enumerable.Range(1, 7).Where(iso => (days & FromIsoNumber(iso)) != 0));

    public static WeekDays ParseStorage(string value)
    {
        var result = WeekDays.None;
        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var iso) || iso is < 1 or > 7)
                throw new FormatException($"Dia da semana inválido: '{token}'. Esperado 1 (segunda) a 7 (domingo).");
            result |= FromIsoNumber(iso);
        }
        return result;
    }
}
