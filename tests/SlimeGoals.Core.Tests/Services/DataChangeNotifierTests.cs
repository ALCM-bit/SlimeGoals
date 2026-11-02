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

    [Fact]
    public void Defer_holds_changes_and_raises_once_when_the_scope_ends()
    {
        var notifier = new DataChangeNotifier();
        var count = 0;
        notifier.Changed += (_, _) => count++;

        using (notifier.Defer())
        {
            notifier.NotifyChanged();
            notifier.NotifyChanged();
            count.Should().Be(0);
        }

        count.Should().Be(1);
    }

    [Fact]
    public void Nested_defer_raises_only_when_the_outermost_scope_ends()
    {
        var notifier = new DataChangeNotifier();
        var count = 0;
        notifier.Changed += (_, _) => count++;

        var outer = notifier.Defer();
        using (notifier.Defer())
            notifier.NotifyChanged();
        count.Should().Be(0);
        outer.Dispose();

        count.Should().Be(1);
    }

    [Fact]
    public void Defer_without_changes_raises_nothing()
    {
        var notifier = new DataChangeNotifier();
        var count = 0;
        notifier.Changed += (_, _) => count++;

        notifier.Defer().Dispose();

        count.Should().Be(0);
    }

    [Fact]
    public void Disposing_a_scope_twice_does_not_close_another()
    {
        var notifier = new DataChangeNotifier();
        var count = 0;
        notifier.Changed += (_, _) => count++;

        var outer = notifier.Defer();
        var inner = notifier.Defer();
        inner.Dispose();
        inner.Dispose();
        notifier.NotifyChanged();
        count.Should().Be(0);
        outer.Dispose();

        count.Should().Be(1);
    }

    [Fact]
    public void NotifyChanged_after_scope_ends_raises_immediately()
    {
        var notifier = new DataChangeNotifier();
        var count = 0;
        notifier.Changed += (_, _) => count++;

        notifier.Defer().Dispose();
        notifier.NotifyChanged();

        count.Should().Be(1);
    }
}
