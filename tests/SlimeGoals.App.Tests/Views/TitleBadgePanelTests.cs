using Avalonia;
using Avalonia.Controls;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>
/// Título da linha do Hoje com o selo "Concluída" logo depois dele (decisão 18): o selo nunca sai da coluna; quem
/// encolhe (e ganha reticências) é o título.
/// </summary>
public class TitleBadgePanelTests
{
    private static (TitleBadgePanel Panel, Border Title, Border Badge) Make(double titleWidth, bool badgeVisible = true)
    {
        var title = new Border { Width = double.NaN, MinWidth = 0, Height = 20 };
        title.Child = new Border { Width = titleWidth, Height = 20 };
        var badge = new Border { Width = 50, Height = 16, IsVisible = badgeVisible };
        var panel = new TitleBadgePanel { Spacing = 12 };
        panel.Children.Add(title);
        panel.Children.Add(badge);
        return (panel, title, badge);
    }

    private static void Layout(TitleBadgePanel panel, double width)
    {
        panel.Measure(new Size(width, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
    }

    [Fact]
    public void Short_title_keeps_the_badge_right_after_it()
    {
        var (panel, title, badge) = Make(100);
        Layout(panel, 300);

        (title.Bounds.Width, badge.Bounds.X).Should().Be((100, 112));
    }

    [Fact]
    public void Long_title_shrinks_and_the_badge_stays_inside()
    {
        var (panel, title, badge) = Make(400);
        Layout(panel, 300);

        (title.Bounds.Width, badge.Bounds.X, badge.Bounds.Right).Should().Be((238, 250, 300));
    }

    [Fact]
    public void Without_the_badge_the_title_takes_the_whole_width()
    {
        var (panel, title, _) = Make(400, badgeVisible: false);
        Layout(panel, 300);

        title.Bounds.Width.Should().Be(300);
    }
}
