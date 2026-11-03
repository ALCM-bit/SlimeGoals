using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

// Datas de nov/2026: 02/11 é segunda, 04/11 quarta, 05/11 quinta.
public class GoalHistoryTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    // Registro às 12:00 UTC (09:00 locais) do dia dado.
    private static Checkin C(Goal goal, string day, int amount = 1) => new()
    {
        Id = Guid.NewGuid(),
        GoalId = goal.Id,
        DoneAt = DateTimeOffset.Parse(day + "T12:00:00Z"),
        Amount = amount,
        UpdatedAt = DateTimeOffset.Parse(day + "T12:00:00Z"),
    };

    private static DailySums Sums(params Checkin[] checkins) => new(checkins, Zone);

    private static Goal DailyGoal() => G(Recurrence.Daily, days: WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday) with
    {
        Target = 1,
        CreatedAt = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void Daily_periods_skip_inactive_days_before_creation_and_after_today()
    {
        var g = DailyGoal();
        var range = new DateRange(D("2026-10-26"), D("2026-11-09"));
        var sums = Sums(C(g, "2026-10-28"), C(g, "2026-10-30"), C(g, "2026-11-02"));

        var periods = GoalHistory.Periods(g, range, D("2026-11-04"), Zone, sums);

        periods.Select(p => p.Range.Start).Should().Equal(D("2026-10-28"), D("2026-10-30"), D("2026-11-02"));
        periods.Should().OnlyContain(p => p.Met);

        var withToday = GoalHistory.Periods(g, range, D("2026-11-04"), Zone,
            Sums(C(g, "2026-10-28"), C(g, "2026-10-30"), C(g, "2026-11-02"), C(g, "2026-11-04")));
        withToday[^1].Range.Start.Should().Be(D("2026-11-04"));
        withToday[^1].Met.Should().BeTrue();
    }

    [Fact]
    public void Daily_met_uses_net_sum()
    {
        var g = DailyGoal();
        var sums = Sums(C(g, "2026-11-02"), C(g, "2026-11-02", -1));

        var periods = GoalHistory.Periods(g, new DateRange(D("2026-11-02"), D("2026-11-03")), D("2026-11-05"), Zone, sums);

        periods.Should().ContainSingle().Which.Should().Match<PeriodResult>(p => p.Done == 0 && !p.Met);
    }

    [Fact]
    public void Daily_counts_until_archive_day_inclusive()
    {
        var g = G(Recurrence.Daily) with
        {
            Target = 1,
            ArchivedAt = new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero),
        };

        GoalHistory.ArchivedOn(g, Zone).Should().Be(D("2026-11-02"));
        GoalHistory.Periods(g, new DateRange(D("2026-11-02"), D("2026-11-09")), D("2026-11-04"), Zone, Sums())
            .Select(p => p.Range.Start).Should().Equal(D("2026-11-02"));
    }

    [Fact]
    public void Weekly_periods_use_clipped_evaluation_and_skip_unmet_current_week()
    {
        var g = G(Recurrence.Weekly, start: "2026-10-21");
        var sums = Sums(C(g, "2026-10-20", 2), C(g, "2026-10-22"), C(g, "2026-10-27", 3), C(g, "2026-11-03"));

        var periods = GoalHistory.Periods(g, new DateRange(D("2026-10-19"), D("2026-11-09")), D("2026-11-04"), Zone, sums);

        periods.Should().Equal(
            new PeriodResult(new DateRange(D("2026-10-19"), D("2026-10-26")), 1, false),
            new PeriodResult(new DateRange(D("2026-10-26"), D("2026-11-02")), 3, true));
    }

    [Fact]
    public void Weekly_counts_week_of_creation()
    {
        var g = G(Recurrence.Weekly) with
        {
            Target = 1,
            CreatedAt = new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero),
        };

        var periods = GoalHistory.Periods(g, new DateRange(D("2026-11-02"), D("2026-11-09")), D("2026-11-06"), Zone,
            Sums(C(g, "2026-11-05")));

        periods.Should().ContainSingle().Which.Should().Be(
            new PeriodResult(new DateRange(D("2026-11-02"), D("2026-11-09")), 1, true));
    }
}
