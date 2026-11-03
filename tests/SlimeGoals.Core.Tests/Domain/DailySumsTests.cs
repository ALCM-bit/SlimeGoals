using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

public class DailySumsTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static Checkin C(Guid goal, string utc, int amount) => new()
    {
        Id = Guid.NewGuid(),
        GoalId = goal,
        DoneAt = DateTimeOffset.Parse(utc + "Z"),
        Amount = amount,
        UpdatedAt = DateTimeOffset.Parse(utc + "Z"),
    };

    [Fact]
    public void Groups_by_local_day_with_signed_sums()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var sums = new DailySums(
            [C(a, "2026-11-03T13:00:00", 2), C(a, "2026-11-04T02:30:00", 1), C(a, "2026-11-04T15:00:00", -1)], Zone);

        sums.On(a, D("2026-11-03")).Should().Be(3);
        sums.On(a, D("2026-11-04")).Should().Be(-1);
        sums.In(a, new DateRange(D("2026-11-03"), D("2026-11-05"))).Should().Be(2);
        sums.On(b, D("2026-11-03")).Should().Be(0);
    }

    [Fact]
    public void PositiveCountOn_counts_only_positive_records_of_given_goals()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var sums = new DailySums(
            [C(a, "2026-11-03T13:00:00", 1), C(a, "2026-11-03T14:00:00", 1), C(a, "2026-11-03T15:00:00", -1),
             C(b, "2026-11-03T16:00:00", 5)], Zone);

        sums.PositiveCountOn(D("2026-11-03"), new HashSet<Guid> { a }).Should().Be(2);
        sums.PositiveCountOn(D("2026-11-03"), new HashSet<Guid> { a, b }).Should().Be(3);
    }
}
