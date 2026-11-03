using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

public class StreaksTests
{
    private static List<PeriodResult> P(params bool[] met) =>
        met.Select((m, i) => new PeriodResult(new DateRange(D("2026-11-01").AddDays(i), D("2026-11-02").AddDays(i)), m ? 1 : 0, m)).ToList();

    [Fact]
    public void Empty_is_zero() => Streaks.From([]).Should().Be(new Streak(0, 0));

    [Fact]
    public void Current_is_trailing_run_and_best_is_longest() =>
        Streaks.From(P(true, true, true, false, true, true)).Should().Be(new Streak(2, 3));

    [Fact]
    public void Trailing_miss_resets_current() =>
        Streaks.From(P(true, true, false)).Should().Be(new Streak(0, 2));
}
