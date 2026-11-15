using Avalonia.Controls;
using SlimeGoals.App.Views;

namespace SlimeGoals.App.Tests.Views;

/// <summary>Alt+F4 ou o menu da janela não destroem a janelinha: ela só se esconde, e volta a aparecer depois.</summary>
public class MiniTimerCloseTests
{
    [Fact]
    public void Closing_by_the_person_hides_instead() =>
        MiniTimerWindow.HidesInsteadOfClosing(WindowCloseReason.WindowClosing, isProgrammatic: false).Should().BeTrue();

    [Theory]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void Shutdown_closes_for_real(WindowCloseReason reason) =>
        MiniTimerWindow.HidesInsteadOfClosing(reason, isProgrammatic: false).Should().BeFalse();

    [Fact]
    public void Closing_from_code_closes_for_real() =>
        MiniTimerWindow.HidesInsteadOfClosing(WindowCloseReason.WindowClosing, isProgrammatic: true).Should().BeFalse();
}
