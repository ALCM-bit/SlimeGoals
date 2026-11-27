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

/// <summary>
/// Os extras da linha (sino com a hora, −, ✕, ⋯) só ficam ocultos até passar o mouse onde há mouse (decisão 18). No
/// Android deitado ou no tablet a linha do desktop aparece, e sem hover eles sumiriam de vez: lá ficam sempre à vista.
/// </summary>
public class TodayRevealTests
{
    [Theory]
    [InlineData(TodayLayoutMode.Top, false, true)]
    [InlineData(TodayLayoutMode.Side, false, true)]
    [InlineData(TodayLayoutMode.Top, true, false)]
    [InlineData(TodayLayoutMode.Side, true, false)]
    [InlineData(TodayLayoutMode.Phone, false, false)]
    public void Extras_hide_at_rest_only_with_a_mouse(TodayLayoutMode mode, bool isAndroid, bool expected) =>
        TodayLayout.HidesExtrasAtRest(mode, isAndroid).Should().Be(expected);
}
