namespace SlimeGoals.Core.Sync;

public interface ISyncTransport
{
    Task<SyncPullPage> PullAsync(long after, int limit, CancellationToken ct = default);
    Task<PushResult> PushAsync(IReadOnlyList<PushItem> items, CancellationToken ct = default);
}
