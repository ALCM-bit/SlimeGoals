using Avalonia;
using Avalonia.Controls;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>
/// A conta (cartão e formulários) ocupa a largura que a coluna oferece, até o MaxWidth da lista, e nunca passa dela:
/// na janela mínima do desktop (900 px) a coluna tem uns 622 px.
/// </summary>
public class FillWidthStackPanelTests
{
    private static FillWidthStackPanel WithNarrowContent()
    {
        var panel = new FillWidthStackPanel { MaxWidth = 640 };
        panel.Children.Add(new Border { Width = 120, Height = 20 });
        return panel;
    }

    [Theory]
    [InlineData(622, 622)]
    [InlineData(900, 640)]
    public void Takes_the_offered_width_up_to_the_max(double offered, double expected)
    {
        var panel = WithNarrowContent();
        panel.Measure(new Size(offered, double.PositiveInfinity));
        panel.DesiredSize.Width.Should().Be(expected);
    }

    [Fact]
    public void Never_asks_more_than_the_column_offers()
    {
        var panel = WithNarrowContent();
        panel.Measure(new Size(622, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 622, panel.DesiredSize.Height));
        panel.Bounds.Width.Should().BeLessThanOrEqualTo(622);
    }

    [Fact]
    public void Without_a_width_limit_uses_the_content()
    {
        var panel = new FillWidthStackPanel();
        panel.Children.Add(new Border { Width = 120, Height = 20 });
        panel.Measure(Size.Infinity);
        panel.DesiredSize.Width.Should().Be(120);
    }

    [Fact]
    public void Is_styled_as_a_StackPanel()
    {
        new FillWidthStackPanel().Should().BeAssignableTo<StackPanel>();
        new FillWidthStackPanel().StyleKey.Should().Be(typeof(StackPanel));
    }
}
