using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.ViewModels;

public class TagStripScrollTests
{
    [Theory]
    [InlineData(0, 300, 300, false)]   // nada transborda
    [InlineData(0, 500, 300, true)]    // há mais à direita
    [InlineData(100, 500, 300, true)]
    [InlineData(200, 500, 300, false)] // no fim
    [InlineData(199.5, 500, 300, false)] // arredondamento: meio pixel do fim já é o fim
    [InlineData(0, 0, 0, false)]
    public void CanScrollRight_only_when_there_is_more_to_the_right(double offset, double extent, double viewport, bool expected) =>
        TagStripScroll.CanScrollRight(offset, extent, viewport).Should().Be(expected);

    [Theory]
    [InlineData(0, 1000, 300, 240)]    // passo de 80% da janela
    [InlineData(600, 1000, 300, 700)]  // não passa do fim
    public void NextOffset_steps_a_page_and_stops_at_the_end(double offset, double extent, double viewport, double expected) =>
        TagStripScroll.NextOffset(offset, extent, viewport).Should().Be(expected);
}
