using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

public class LocalTimeTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void Plain_time_converts_with_zone_offset()
    {
        LocalTime.ToUtc(D("2026-11-04"), new TimeOnly(8, 30), SaoPaulo)
            .Should().Be(new DateTimeOffset(2026, 11, 4, 11, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Time_in_dst_gap_becomes_first_valid_instant()
    {
        // 08/03/2026: 02:00 → 03:00 em Nova York; 02:30 não existe.
        LocalTime.ToUtc(D("2026-03-08"), new TimeOnly(2, 30), NewYork)
            .Should().Be(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Repeated_time_uses_first_occurrence()
    {
        // 01/11/2026: 02:00 → 01:00 em Nova York; 01:30 ocorre duas vezes (05:30 UTC e 06:30 UTC).
        LocalTime.ToUtc(D("2026-11-01"), new TimeOnly(1, 30), NewYork)
            .Should().Be(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero));
    }
}
