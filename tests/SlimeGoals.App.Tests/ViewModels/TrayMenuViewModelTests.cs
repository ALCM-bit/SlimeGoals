using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class TrayMenuViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 4, 15, 0, 0, TimeSpan.Zero));
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly FakeWindow _window = new();
    private readonly FakeLifetime _lifetime = new();

    private TrayMenuViewModel NewTray(out ReminderPause pause)
    {
        pause = new ReminderPause(_settings, _time, _changes);
        return new TrayMenuViewModel(_window, pause, _lifetime, _changes);
    }

    [Fact]
    public void Open_activates_window()
    {
        var tray = NewTray(out _);

        tray.OpenCommand.Execute(null);

        _window.Activations.Should().Be(1);
    }

    [Fact]
    public async Task Toggle_pause_switches_text_after_change()
    {
        var tray = NewTray(out var pause);
        await tray.RefreshAsync();
        tray.PauseText.Should().Be("Pausar lembretes");

        await tray.TogglePauseCommand.ExecuteAsync(null);

        (await pause.GetAsync()).IsPaused.Should().BeTrue();
        tray.PauseText.Should().Be("Retomar lembretes");

        await tray.TogglePauseCommand.ExecuteAsync(null);

        (await pause.GetAsync()).IsPaused.Should().BeFalse();
        tray.PauseText.Should().Be("Pausar lembretes");
    }

    [Fact]
    public async Task Pause_from_elsewhere_updates_text()
    {
        var tray = NewTray(out var pause);
        await tray.RefreshAsync();

        await pause.PauseAsync();
        await WaitUntil(() => tray.PauseText == "Retomar lembretes");

        tray.PauseText.Should().Be("Retomar lembretes");
    }

    [Fact]
    public void Quit_calls_lifetime()
    {
        var tray = NewTray(out _);

        tray.QuitCommand.Execute(null);

        _lifetime.Quits.Should().Be(1);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(10);
    }

    private sealed class FakeWindow : IWindowActivator
    {
        public int Activations { get; private set; }

        public void Activate() => Activations++;
    }

    private sealed class FakeLifetime : IAppLifetime
    {
        public int Quits { get; private set; }

        public void Quit() => Quits++;
    }
}
