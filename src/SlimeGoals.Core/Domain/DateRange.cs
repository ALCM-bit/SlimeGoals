namespace SlimeGoals.Core.Domain;

/// <summary>
/// Intervalo semiaberto [Start, EndExclusive) de datas locais.
/// DateOnly.MinValue / DateOnly.MaxValue representam limites abertos.
/// </summary>
public readonly record struct DateRange(DateOnly Start, DateOnly EndExclusive)
{
    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;
}
