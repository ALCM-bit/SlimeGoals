using SlimeGoals.App.Account;
using SlimeGoals.Sync;

namespace SlimeGoals.App.Tests.Account;

public class SyncStaleWarningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static AccountStatus Status(AccountState state, DateTimeOffset? lastSync) =>
        new(state, "ana@exemplo.com", new Uri("https://sync.exemplo.com/"), lastSync, 0, null);

    [Fact]
    public void Stale_after_more_than_three_days_signed_in() =>
        SyncStaleWarning.IsStale(Status(AccountState.SignedIn, Now - TimeSpan.FromDays(3) - TimeSpan.FromSeconds(1)), Now)
            .Should().BeTrue();

    [Fact]
    public void Not_stale_at_exactly_three_days() =>
        SyncStaleWarning.IsStale(Status(AccountState.SignedIn, Now - TimeSpan.FromDays(3)), Now).Should().BeFalse();

    [Fact]
    public void Not_stale_when_signed_out() =>
        SyncStaleWarning.IsStale(Status(AccountState.SignedOut, Now - TimeSpan.FromDays(30)), Now).Should().BeFalse();

    [Fact]
    public void Stale_when_the_session_expired_long_ago() =>
        SyncStaleWarning.IsStale(Status(AccountState.SessionExpired, Now - TimeSpan.FromDays(4)), Now).Should().BeTrue();

    [Fact]
    public void No_warning_before_the_first_successful_sync() =>
        SyncStaleWarning.IsStale(Status(AccountState.SignedIn, null), Now).Should().BeFalse();

    [Fact]
    public void No_warning_when_the_last_sync_is_in_the_future() =>
        SyncStaleWarning.IsStale(Status(AccountState.SignedIn, Now + TimeSpan.FromDays(1)), Now).Should().BeFalse();

    [Fact]
    public void Text_points_to_the_connection_unless_the_app_failed_the_security_check()
    {
        var last = Now - TimeSpan.FromDays(4);

        SyncStaleWarning.TextFor(Status(AccountState.SignedIn, last), Now).Should().Be(SyncStaleWarning.Text);
        SyncStaleWarning.TextFor(Status(AccountState.UntrustedApp, last), Now).Should().Be(SyncStaleWarning.UntrustedText);
        SyncStaleWarning.TextFor(Status(AccountState.SignedIn, Now), Now).Should().BeNull();
    }
}
