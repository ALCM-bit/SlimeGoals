using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

public class TodayLayoutTests
{
    [Theory]
    [InlineData(412, 412, TodayLayoutMode.Phone)]
    [InlineData(719, 600, TodayLayoutMode.Phone)]
    [InlineData(720, 500, TodayLayoutMode.Top)]
    [InlineData(1100, 838, TodayLayoutMode.Top)]
    [InlineData(1220, 980, TodayLayoutMode.Side)]
    [InlineData(1440, 1178, TodayLayoutMode.Side)]
    public void Mode_follows_window_and_page_width(double window, double page, TodayLayoutMode expected) =>
        TodayLayout.For(window, page).Should().Be(expected);
}
