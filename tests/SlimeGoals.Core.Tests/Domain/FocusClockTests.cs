using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class FocusClockTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static FocusSession Session(int step = 0, DateTimeOffset? pausedAt = null, TimeSpan? carry = null) =>
        new(FocusPlan.Default, null, T, step, T, T, carry ?? TimeSpan.Zero, pausedAt, null, null, null, 0);

    [Fact]
    public void Clock_counts_down_and_freezes_while_paused()
    {
        FocusClock.Remaining(Session(), T.AddMinutes(10)).Should().Be(TimeSpan.FromMinutes(15));

        var paused = Session(pausedAt: T.AddMinutes(10));
        FocusClock.Remaining(paused, T.AddMinutes(50)).Should().Be(TimeSpan.FromMinutes(15));
        FocusClock.IsWaiting(paused, T.AddMinutes(50)).Should().BeFalse();
    }

    [Fact]
    public void Overtime_and_review_start_after_the_end()
    {
        var now = T.AddMinutes(25).AddSeconds(59);
        FocusClock.Overtime(Session(), now).Should().Be(TimeSpan.FromSeconds(59));
        FocusClock.IsWaiting(Session(), now).Should().BeTrue();
        FocusClock.NeedsReview(Session(), now).Should().BeFalse();
        FocusClock.NeedsReview(Session(), T.AddMinutes(55)).Should().BeTrue();
        FocusClock.NeedsReview(Session(step: 1), T.AddMinutes(200)).Should().BeFalse();
    }

    [Fact]
    public void Open_focus_adds_carry_and_ignores_breaks()
    {
        var carry = TimeSpan.FromSeconds(40);
        FocusClock.OpenFocus(Session(carry: carry), T.AddMinutes(3)).Should().Be(TimeSpan.FromMinutes(3) + carry);
        FocusClock.OpenFocus(Session(step: 1, carry: carry), T.AddMinutes(3)).Should().Be(carry);
    }

    [Fact]
    public void CutSeconds_truncates_mid_session_and_rounds_last()
    {
        var open = new TimeSpan(0, 0, 2, 40, 600);
        FocusClock.CutSeconds(open, false).Should().Be(160);
        FocusClock.CutSeconds(open, true).Should().Be(161);
        FocusClock.CutSeconds(new TimeSpan(0, 0, 2, 40, 400), true).Should().Be(160);
        FocusClock.CutSeconds(TimeSpan.FromMilliseconds(499), true).Should().Be(0);
        FocusClock.CutSeconds(TimeSpan.FromMilliseconds(500), true).Should().Be(1);
    }
}
