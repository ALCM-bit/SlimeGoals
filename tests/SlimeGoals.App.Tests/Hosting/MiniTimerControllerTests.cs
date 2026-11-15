using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Support;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Tests.Hosting;

public sealed class MiniTimerControllerTests : IDisposable
{
    private readonly TimerFixture _f = new();
    private readonly FakePresence _main = new();
    private readonly FakeMiniWindow _window = new();

    public void Dispose() => _f.Dispose();

    private async Task<MiniTimerController> StartedAsync()
    {
        var controller = new MiniTimerController(_f.Watcher, _main, _window, _f.Settings, _f.Changes, a => a());
        await controller.StartAsync();
        return controller;
    }

    [Fact]
    public async Task Shows_when_a_timer_runs_and_the_main_window_is_away()
    {
        var goal = await _f.StudyAsync();
        _main.Set(onScreen: true);
        await StartedAsync();

        await _f.Timers.StartAsync(goal.Id);
        _window.Calls.Should().BeEmpty("a janela principal está à vista");

        _main.Set(onScreen: false);
        _window.Calls.Should().Equal("show default");
    }

    [Fact]
    public async Task Hides_when_the_main_window_comes_back()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();

        _main.Set(onScreen: true);

        _window.Calls.Should().Equal("show default", "hide");
    }

    [Fact]
    public async Task Hides_when_the_timer_stops()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();

        await _f.Timers.StopAsync();

        _window.Calls.Should().Equal("show default", "hide");
    }

    [Fact]
    public async Task Stays_hidden_while_the_option_is_off_and_follows_it_when_toggled()
    {
        var goal = await _f.StudyAsync();
        await _f.Settings.SetMiniTimerEnabledAsync(false);
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();
        _window.Calls.Should().BeEmpty();

        await _f.Settings.SetMiniTimerEnabledAsync(true);
        _window.Calls.Should().Equal("show default");

        await _f.Settings.SetMiniTimerEnabledAsync(false);
        _window.Calls.Should().Equal("show default", "hide");
    }

    [Fact]
    public async Task Shows_at_startup_in_background_with_a_timer_already_running()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        _main.Set(onScreen: false); // --background: a janela principal nunca abriu

        await StartedAsync();

        _window.Calls.Should().Equal("show default");
    }

    [Fact]
    public async Task Saved_position_is_used_and_a_drop_saves_the_new_one()
    {
        var goal = await _f.StudyAsync();
        await _f.Settings.SetMiniTimerPositionAsync(100, 200);
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();
        _window.Calls.Should().Equal("show 100,200");

        _window.Drop(300, 400);
        (await _f.Settings.GetMiniTimerPositionAsync()).Should().Be((300, 400));

        _main.Set(onScreen: true);
        _main.Set(onScreen: false);
        _window.Calls.Last().Should().Be("show 300,400");
    }

    [Fact]
    public async Task Nothing_is_created_without_a_running_timer()
    {
        await _f.StudyAsync();
        _main.Set(onScreen: false);

        await StartedAsync();
        _main.Set(onScreen: true);
        _main.Set(onScreen: false);

        _window.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_hides_the_window()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        var controller = await StartedAsync();

        await controller.StopAsync();
        await _f.Timers.StopAsync();

        _window.Calls.Should().Equal("show default", "hide");
    }

    [Fact]
    public async Task Hiding_from_the_window_keeps_it_hidden_for_this_timer()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();

        _window.Dismiss();
        _main.Set(onScreen: true);
        _main.Set(onScreen: false);

        _window.Calls.Should().Equal("show default", "hide");
    }

    [Fact]
    public async Task The_next_timer_shows_the_window_again()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();
        _window.Dismiss();

        await _f.Timers.StopAsync();
        await _f.Timers.DiscardAsync();
        _f.Time.Advance(TimeSpan.FromMinutes(1));
        await _f.Timers.StartAsync(goal.Id);

        _window.Calls.Should().Equal("show default", "hide", "show default");
    }

    [Fact]
    public async Task Hiding_from_the_window_keeps_the_option_on()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await StartedAsync();

        _window.Dismiss();

        (await _f.Settings.GetMiniTimerEnabledAsync()).Should().BeTrue();
    }

    private sealed class FakePresence : IMainWindowPresence
    {
        public bool IsOnScreen { get; private set; }
        public event EventHandler? Changed;

        public void Set(bool onScreen)
        {
            IsOnScreen = onScreen;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeMiniWindow : IMiniTimerWindow
    {
        public List<string> Calls { get; } = [];
        public event EventHandler<(int X, int Y)>? Moved;

        public void Show((int X, int Y)? position) =>
            Calls.Add(position is { } p ? $"show {p.X},{p.Y}" : "show default");

        public void Hide() => Calls.Add("hide");

        public event EventHandler? HideRequested;

        public void Drop(int x, int y) => Moved?.Invoke(this, (x, y));

        public void Dismiss() => HideRequested?.Invoke(this, EventArgs.Empty);
    }
}
