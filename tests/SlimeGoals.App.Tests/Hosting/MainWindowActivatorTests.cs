using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.Tests.Hosting;

public class MainWindowActivatorTests
{
    [Fact]
    public void Activate_without_window_is_ignored()
    {
        // Sem plataforma Avalonia iniciada (como antes de AppMain): retorna antes de tocar no Dispatcher.
        var activator = new MainWindowActivator();

        var act = activator.Activate;

        act.Should().NotThrow();
        activator.Window.Should().BeNull();
    }
}
