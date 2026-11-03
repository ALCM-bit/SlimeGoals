namespace SlimeGoals.Core.Domain;

/// <summary>
/// Somas com sinal dos registros por (meta, dia local), calculadas uma vez para os relatórios.
/// Também guarda quantos registros positivos cada meta teve no dia.
/// </summary>
public sealed class DailySums
{
    private readonly Dictionary<(Guid GoalId, DateOnly Day), int> _sums = [];
    private readonly Dictionary<(Guid GoalId, DateOnly Day), int> _positives = [];

    public DailySums(IEnumerable<Checkin> checkins, TimeZoneInfo zone)
    {
        foreach (var c in checkins)
        {
            var key = (c.GoalId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.DoneAt, zone).DateTime));
            _sums[key] = _sums.GetValueOrDefault(key) + c.Amount;
            if (c.Amount > 0)
                _positives[key] = _positives.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>Soma com sinal da meta no dia local.</summary>
    public int On(Guid goalId, DateOnly day) => _sums.GetValueOrDefault((goalId, day));

    /// <summary>Soma da meta nos dias de [Start, EndExclusive).</summary>
    public int In(Guid goalId, DateRange range) =>
        _sums.Where(kv => kv.Key.GoalId == goalId && range.Contains(kv.Key.Day)).Sum(kv => kv.Value);

    /// <summary>Registros com Amount &gt; 0 no dia, somando as metas dadas.</summary>
    public int PositiveCountOn(DateOnly day, IReadOnlySet<Guid> goalIds) =>
        goalIds.Sum(id => _positives.GetValueOrDefault((id, day)));
}
