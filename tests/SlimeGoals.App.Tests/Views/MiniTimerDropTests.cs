using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>O fim do arrasto da janelinha: a posição parada por um tempo vale como soltura.</summary>
public class MiniTimerDropTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly List<(int X, int Y)> _dropped = [];
    private readonly MiniTimerDrop _drop;

    public MiniTimerDropTests() => _drop = new MiniTimerDrop(_time, _dropped.Add);

    [Fact]
    public void The_position_still_for_the_delay_counts_as_dropped()
    {
        _drop.Begin();
        _drop.Moved((10, 20));
        _time.Advance(MiniTimerDrop.Delay - TimeSpan.FromMilliseconds(1));
        _dropped.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _dropped.Should().Equal((10, 20));
    }

    [Fact]
    public void A_pause_in_the_middle_of_a_drag_does_not_end_it()
    {
        _drop.Begin();
        _drop.Moved((10, 20));
        _time.Advance(MiniTimerDrop.Delay);
        _drop.Moved((500, 600));
        _time.Advance(MiniTimerDrop.Delay);

        _dropped.Should().Equal((10, 20), (500, 600));
    }

    [Fact]
    public void Moves_without_a_drag_are_ignored()
    {
        _drop.Moved((10, 20));
        _time.Advance(MiniTimerDrop.Delay);

        _dropped.Should().BeEmpty();
    }

    [Fact]
    public void Reset_ends_the_drag_and_drops_nothing_pending()
    {
        _drop.Begin();
        _drop.Moved((10, 20));
        _drop.Reset();
        _drop.Moved((30, 40));
        _time.Advance(MiniTimerDrop.Delay);

        _dropped.Should().BeEmpty();
    }
}
