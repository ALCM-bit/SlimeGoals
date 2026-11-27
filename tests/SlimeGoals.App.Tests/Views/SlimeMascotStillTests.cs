using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>O slime do logo da barra lateral fica parado: só o do Hoje anima ao entrar na tela.</summary>
public class SlimeMascotStillTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public void Animates_only_when_on_screen_and_not_still(bool isStill, bool onScreen, bool expected) =>
        SlimeMascot.Animates(isStill, onScreen).Should().Be(expected);
}
