using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

public class UiScaleTests
{
    [Theory]
    [InlineData(900, 600, 1.0)]
    [InlineData(1100, 720, 1.0)]
    [InlineData(1280, 800, 1.0)]
    [InlineData(1440, 900, 1.125)]
    [InlineData(1920, 1080, 1.35)]
    [InlineData(2560, 1000, 1.25)]
    [InlineData(1400, 1400, 1.09375)]
    public void Scale_follows_the_smaller_ratio_of_the_window(double width, double height, double expected) =>
        UiScale.For(width, height).Should().BeApproximately(expected, 0.0001);

    [Theory]
    [InlineData(2544, 1440)]
    [InlineData(3840, 2160)]
    public void Scale_stops_at_the_maximum(double width, double height) =>
        UiScale.For(width, height).Should().Be(UiScale.Maximum);

    [Fact]
    public void Scale_never_shrinks_the_design_size() =>
        UiScale.For(320, 480).Should().Be(1.0);
}
