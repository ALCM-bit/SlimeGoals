using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

public class GoalTotalsTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void Completion_is_limited_to_zero_and_one(int total, int expected) =>
        GoalTotals.Effective(G(Recurrence.Daily) with { Measure = Measure.Completion, Target = 1 }, total).Should().Be(expected);

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(5, 5)]
    [InlineData(12, 12)]
    public void Quantity_has_a_floor_of_zero_and_no_ceiling(int total, int expected) =>
        GoalTotals.Effective(G(Recurrence.Daily) with { Measure = Measure.Quantity, Target = 8 }, total).Should().Be(expected);

    [Fact]
    public void Duration_has_a_floor_of_zero() =>
        GoalTotals.Effective(G(Recurrence.Daily) with { Measure = Measure.Duration, Target = 30 }, -1).Should().Be(0);
}
