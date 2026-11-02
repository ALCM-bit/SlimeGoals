namespace SlimeGoals.Core.Domain;

/// <summary>Conversões de data e hora locais para instantes UTC.</summary>
public static class LocalTime
{
    /// <summary>
    /// Primeiro instante UTC cuja hora local é ≥ <paramref name="date"/> + <paramref name="time"/>.
    /// Hora inexistente (horário de verão) → primeiro instante válido depois; hora repetida → primeira ocorrência.
    /// </summary>
    /// <remarks>
    /// A busca é feita pelo lado UTC (UTC → local é sempre bem definido), porque
    /// IsInvalidTime/GetUtcOffset no lado local erram em alguns fusos reais (ex.: America/Asuncion, 2023).
    /// </remarks>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var target = date.ToDateTime(time);
        bool Reached(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone).DateTime >= target;

        // Nenhum fuso passa de UTC+14: 15 h antes do alvo "em UTC" ainda é antes dele.
        var utc = new DateTimeOffset(target, TimeSpan.Zero).AddHours(-15);
        while (!Reached(utc))
            utc = utc.AddMinutes(15);

        // Refina minuto a minuto dentro do último passo (transições ocorrem em minutos inteiros).
        var candidate = utc.AddMinutes(-15);
        while (!Reached(candidate))
            candidate = candidate.AddMinutes(1);
        return candidate;
    }
}
