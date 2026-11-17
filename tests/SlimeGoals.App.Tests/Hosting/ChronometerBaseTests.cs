using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.Tests.Hosting;

/// <summary>O início do cronômetro no relógio "desde o boot", que o Chronometer do Android usa.</summary>
public class ChronometerBaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void The_base_is_the_boot_clock_minus_the_elapsed_time() =>
        ChronometerBase.Of(Now.AddMinutes(-23).AddSeconds(-51), Now, bootClockMs: 5_000_000)
            .Should().Be(5_000_000 - (23 * 60 + 51) * 1000);

    [Fact]
    public void A_start_in_the_future_counts_from_zero() =>
        ChronometerBase.Of(Now.AddMinutes(5), Now, bootClockMs: 5_000_000).Should().Be(5_000_000);

    [Fact]
    public void A_start_before_the_boot_gives_a_negative_base() =>
        ChronometerBase.Of(Now.AddHours(-2), Now, bootClockMs: 60_000).Should().Be(60_000 - 2 * 3_600_000);

    [Fact]
    public void The_countdown_base_is_the_boot_clock_plus_the_remaining_time() =>
        ChronometerBase.CountdownOf(Now.AddMinutes(24).AddSeconds(10), Now, bootClockMs: 5_000_000)
            .Should().Be(5_000_000 + (24 * 60 + 10) * 1000);

    [Fact]
    public void A_countdown_whose_end_has_passed_stays_at_zero() =>
        ChronometerBase.CountdownOf(Now.AddSeconds(-30), Now, bootClockMs: 5_000_000).Should().Be(5_000_000);
}
