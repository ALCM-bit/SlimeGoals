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
    /// Primeiro instante do dia local em UTC. Trata o horário de verão:
    /// meia-noite inexistente → primeiro instante válido; meia-noite repetida → primeira ocorrência.
    /// </summary>
    public static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);

        // Na hora repetida, o maior offset corresponde à primeira ocorrência (antes de recuar o relógio).
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
