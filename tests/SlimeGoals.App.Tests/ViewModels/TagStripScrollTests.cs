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

    [Theory]
    [InlineData(0, 300, 50, 150, 0)]      // já visível: não mexe
    [InlineData(100, 300, 150, 250, 100)] // visível dentro da janela deslocada
    [InlineData(0, 300, 400, 500, 400)]   // além da direita: a borda esquerda do chip vira o começo
    [InlineData(500, 300, 100, 200, 100)] // antes da esquerda: idem
    [InlineData(0, 300, 250, 350, 250)]   // cortado na ponta direita
    public void RevealOffset_moves_only_when_the_chip_is_not_fully_visible(double offset, double viewport, double left, double right, double expected) =>
        TagStripScroll.RevealOffset(offset, viewport, left, right).Should().Be(expected);
}
