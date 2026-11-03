namespace SlimeGoals.Core.Domain;

/// <summary>Somas com sinal dos registros por (meta, dia local), calculadas uma vez para os relatórios.</summary>
public sealed class DailySums
{
    private readonly Dictionary<(Guid GoalId, DateOnly Day), int> _sums = [];

    public DailySums(IEnumerable<Checkin> checkins, TimeZoneInfo zone)
    {
        foreach (var c in checkins)
        {
            var key = (c.GoalId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.DoneAt, zone).DateTime));
            _sums[key] = _sums.GetValueOrDefault(key) + c.Amount;
        }
    }

    /// <summary>Soma com sinal da meta no dia local.</summary>
    public int On(Guid goalId, DateOnly day) => _sums.GetValueOrDefault((goalId, day));

    /// <summary>Soma da meta nos dias de [Start, EndExclusive).</summary>
    public int In(Guid goalId, DateRange range) =>
        _sums.Where(kv => kv.Key.GoalId == goalId && range.Contains(kv.Key.Day)).Sum(kv => kv.Value);
}
