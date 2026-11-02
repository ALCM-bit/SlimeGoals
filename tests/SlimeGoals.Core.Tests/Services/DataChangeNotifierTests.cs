using SlimeGoals.Core.Services;

namespace SlimeGoals.Core.Tests.Services;

public class DataChangeNotifierTests
{
    [Fact]
    public void NotifyChanged_raises_event_for_each_subscriber()
    {
        var notifier = new DataChangeNotifier();
        var a = 0;
        var b = 0;
        notifier.Changed += (_, _) => a++;
        notifier.Changed += (_, _) => b++;

        notifier.NotifyChanged();

        (a, b).Should().Be((1, 1));
    }

    [Fact]
    public void NotifyChanged_without_subscribers_does_not_throw()
    {
        var act = () => new DataChangeNotifier().NotifyChanged();

        act.Should().NotThrow();
    }

    [Fact]
    public void Throwing_subscriber_does_not_stop_others_or_escape()
    {
        var notifier = new DataChangeNotifier();
        var called = 0;
        notifier.Changed += (_, _) => throw new InvalidOperationException("boom");
        notifier.Changed += (_, _) => called++;

        var act = notifier.NotifyChanged;

        act.Should().NotThrow();
        called.Should().Be(1);
    }
}
