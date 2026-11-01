using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

// Datas de nov/2026: 02/11 é segunda, 07/11 é sábado, 08/11 é domingo.
public class GoalPeriodTests
{
    [Fact]
    public void Weekly_period_is_monday_to_next_monday() =>
        GoalPeriod.EvaluationPeriod(G(Recurrence.Weekly), D("2026-11-08"))
            .Should().Be(new DateRange(D("2026-11-02"), D("2026-11-09")));

    [Fact]
    public void Weekly_period_is_clipped_by_start_and_end() =>
        GoalPeriod.EvaluationPeriod(G(Recurrence.Weekly, start: "2026-11-04", end: "2026-11-06"), D("2026-11-05"))
            .Should().Be(new DateRange(D("2026-11-04"), D("2026-11-07")));

    [Fact]
    public void Daily_period_is_the_day() =>
        GoalPeriod.EvaluationPeriod(G(Recurrence.Daily), D("2026-11-05"))
            .Should().Be(new DateRange(D("2026-11-05"), D("2026-11-06")));


    [Theory]
    [InlineData("2026-11-03")]
    [InlineData("2026-11-11")]
    public void Outside_start_end_has_no_period_and_is_not_shown(string date)
    {
        var g = G(Recurrence.Weekly, start: "2026-11-04", end: "2026-11-10");
        GoalPeriod.EvaluationPeriod(g, D(date)).Should().BeNull();
        GoalPeriod.IsShownOn(g, D(date)).Should().BeFalse();
    }

    [Fact]
    public void Daily_is_hidden_on_inactive_day_but_weekly_is_shown()
    {
        GoalPeriod.IsShownOn(G(Recurrence.Daily, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeFalse();
        GoalPeriod.IsShownOn(G(Recurrence.Weekly, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeTrue();
        GoalPeriod.RemindsOn(G(Recurrence.Weekly, days: WeekDays.Weekdays), D("2026-11-07")).Should().BeFalse();
    }

    [Fact]
    public void End_date_is_inclusive() =>
        GoalPeriod.IsInPeriod(G(Recurrence.Daily, end: "2026-11-10"), D("2026-11-10")).Should().BeTrue();

    [Fact]
    public void Weekly_ending_on_max_date_does_not_overflow() =>
        GoalPeriod.EvaluationPeriod(G(Recurrence.Weekly, end: DateOnly.MaxValue.ToString("yyyy-MM-dd")), D("2026-11-05"))
            .Should().Be(new DateRange(D("2026-11-02"), D("2026-11-09")));
}
