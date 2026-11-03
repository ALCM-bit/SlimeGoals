using SlimeGoals.Core.Services;
using SlimeGoals.Platform.Linux.Power;

namespace SlimeGoals.Platform.Linux.Tests;

public class SleepResumeWatcherTests
{
    private sealed class FakeSource : ISleepSignalSource
    {
        public Action<bool>? Callback;
        public bool Throw;

        public Task StartAsync(Action<bool> onPrepareForSleep, CancellationToken ct = default)
        {
            if (Throw) throw new InvalidOperationException("sem logind");
            Callback = onPrepareForSleep;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static (SleepResumeWatcher Watcher, FakeSource Source, Func<int> Count) Create(bool fail = false)
    {
        var source = new FakeSource { Throw = fail };
        var changes = new DataChangeNotifier();
        var count = 0;
        changes.Changed += (_, _) => count++;
        return (new SleepResumeWatcher(source, changes), source, () => count);
    }

    [Fact]
    public async Task Waking_up_notifies_data_change()
    {
        var (watcher, source, count) = Create();
        await watcher.StartAsync();
        source.Callback!(false);
        Assert.Equal(1, count());
    }

    [Fact]
    public async Task Going_to_sleep_does_not_notify()
    {
        var (watcher, source, count) = Create();
        await watcher.StartAsync();
        source.Callback!(true);
        Assert.Equal(0, count());
    }

    [Fact]
    public async Task Source_failure_on_start_is_swallowed()
    {
        var (watcher, _, _) = Create(fail: true);
        await watcher.StartAsync();
    }

    [Fact]
    public async Task Disposing_stops_notifications()
    {
        var (watcher, source, count) = Create();
        await watcher.StartAsync();
        await watcher.DisposeAsync();
        source.Callback!(false);
        Assert.Equal(0, count());
    }
}
