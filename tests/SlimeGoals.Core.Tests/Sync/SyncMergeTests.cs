using SlimeGoals.Core.Sync;

namespace SlimeGoals.Core.Tests.Sync;

public class SyncMergeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Small = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Large = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void Newer_local_wins() =>
        SyncMerge.LocalWins(T0.AddSeconds(1), Small, T0, Large).Should().BeTrue();

    [Fact]
    public void Newer_remote_wins() =>
        SyncMerge.LocalWins(T0, Large, T0.AddSeconds(1), Small).Should().BeFalse();

    [Fact]
    public void Tie_goes_to_larger_device_id()
    {
        SyncMerge.LocalWins(T0, Large, T0, Small).Should().BeTrue();
        SyncMerge.LocalWins(T0, Small, T0, Large).Should().BeFalse();
    }

    [Fact]
    public void Same_version_is_not_a_local_win() =>
        SyncMerge.LocalWins(T0, Small, T0, Small).Should().BeFalse();
}
