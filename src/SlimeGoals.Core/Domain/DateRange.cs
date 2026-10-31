namespace SlimeGoals.Core.Domain;

/// <summary>
/// Intervalo semiaberto [Start, EndExclusive) de datas locais.
/// DateOnly.MinValue / DateOnly.MaxValue representam limites abertos.
/// </summary>
public readonly record struct DateRange(DateOnly Start, DateOnly EndExclusive)
{
    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;

    /// <summary>
    /// Converte o intervalo local para instantes UTC, para filtrar colunas como checkins.done_at.
    /// Limites abertos viram DateTimeOffset.MinValue / MaxValue.
    /// </summary>
    public (DateTimeOffset StartUtc, DateTimeOffset EndUtc) ToUtc(TimeZoneInfo zone) =>
        (Start == DateOnly.MinValue ? DateTimeOffset.MinValue : LocalMidnightToUtc(Start, zone),
         EndExclusive == DateOnly.MaxValue ? DateTimeOffset.MaxValue : LocalMidnightToUtc(EndExclusive, zone));

    /// <summary>
    /// Primeiro instante do dia local, em UTC. Trata o horário de verão:
    /// meia-noite inexistente → primeiro instante válido; meia-noite repetida → primeira ocorrência.
    /// </summary>
    /// <remarks>
    /// A busca é feita pelo lado UTC (UTC → local é sempre bem definido), porque
    /// IsInvalidTime/GetUtcOffset no lado local erram em alguns fusos reais (ex.: America/Asuncion, 2023).
    /// </remarks>
    public static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        bool ReachedDay(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, zone).DateTime >= midnight;

        // Nenhum fuso passa de UTC+14: 15 h antes da meia-noite "em UTC" ainda é o dia anterior.
        var utc = new DateTimeOffset(midnight, TimeSpan.Zero).AddHours(-15);
        while (!ReachedDay(utc))
            utc = utc.AddMinutes(15);

        // Refina minuto a minuto dentro do último passo (transições ocorrem em minutos inteiros).
        var candidate = utc.AddMinutes(-15);
        while (!ReachedDay(candidate))
            candidate = candidate.AddMinutes(1);
        return candidate;
    }
}
