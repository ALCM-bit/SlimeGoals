using SlimeGoals.Core.Domain;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Domain;

public class DateRangeUtcTests
{
    private static TimeZoneInfo Fixed(int hours) =>
        TimeZoneInfo.CreateCustomTimeZone($"Test/Fixed{hours}", TimeSpan.FromHours(hours), "fixed", "fixed");

    /// <summary>
    /// Fuso -03:00 com horário de verão (+1 h) cujo início pula a meia-noite (00:00 → 01:00,
    /// 1º domingo de novembro) e cujo fim repete a meia-noite (01:00 de verão → 00:00,
    /// 3º domingo de fevereiro). Em 2026/27: 01/11/2026 e 21/02/2027.
    /// </summary>
    private static readonly TimeZoneInfo MidnightDst = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MidnightDst", TimeSpan.FromHours(-3), "Midnight DST", "STD", "DST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 11, 1, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 2, 3, DayOfWeek.Sunday)),
        ]);

    [Fact]
    public void Plain_day_in_utc_minus_3()
    {
        var r = new DateRange(D("2026-11-03"), D("2026-11-04")).ToUtc(Fixed(-3));
        r.StartUtc.Should().Be(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero));
        r.EndUtc.Should().Be(new DateTimeOffset(2026, 11, 4, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact] // 2026-11-01 00:00 não existe → 01:00 (-02:00)
    public void Skipped_midnight_uses_first_valid_instant() =>
        DateRange.LocalMidnightToUtc(D("2026-11-01"), MidnightDst)
            .Should().Be(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero));

    [Fact] // 2027-02-21 00:00 ocorre duas vezes → a primeira (-02:00)
    public void Repeated_midnight_uses_first_occurrence() =>
        DateRange.LocalMidnightToUtc(D("2027-02-21"), MidnightDst)
            .Should().Be(new DateTimeOffset(2027, 2, 21, 2, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Open_bounds_map_to_extremes() =>
        new DateRange(DateOnly.MinValue, DateOnly.MaxValue).ToUtc(Fixed(-3))
            .Should().Be((DateTimeOffset.MinValue, DateTimeOffset.MaxValue));
}
