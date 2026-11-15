using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Support;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Tests.ViewModels;

public sealed class MiniTimerViewModelTests : IDisposable
{
    private readonly TimerFixture _f = new();
    private readonly FakeActivator _window = new();
    private readonly StubPage _today = new("Hoje");
    private readonly StubPage _goals = new("Metas");
    private readonly ShellViewModel _shell;

    public MiniTimerViewModelTests() => _shell = new ShellViewModel([_today, _goals]);

    public void Dispose() => _f.Dispose();

    private async Task<MiniTimerViewModel> RunningAsync()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await _f.Watcher.StartAsync();
        return new MiniTimerViewModel(_f.Watcher, _f.Timers, _window, _shell, _f.Time);
    }

    [Fact]
    public async Task Shows_the_running_goal()
    {
        var vm = await RunningAsync();

        vm.Title.Should().Be("Estudar inglês");
        vm.IconKey.Should().Be("book");
        vm.Color.Should().Be(GoalColor.Green);
        vm.Elapsed.Should().Be("0:00:00");
    }

    [Fact]
    public async Task Elapsed_ticks_every_second_and_stops_ticking_without_a_timer()
    {
        var vm = await RunningAsync();

        _f.Time.Advance(TimeSpan.FromSeconds(61));
        vm.Elapsed.Should().Be("0:01:01");

        await _f.Timers.StopAsync();
        using var monitor = vm.Monitor();
        _f.Time.Advance(TimeSpan.FromSeconds(5));

        vm.Elapsed.Should().Be("0:01:01");
        monitor.Should().NotRaisePropertyChangeFor(x => x.Elapsed);
    }

    [Fact]
    public async Task Title_follows_a_rename()
    {
        var vm = await RunningAsync();
        var goal = (await _f.GoalService.ListAsync()).Single();

        await _f.GoalService.UpdateAsync(goal.Id, new GoalDraft("Estudar alemão", goal.Recurrence, goal.Target, goal.Icon, goal.Color,
            goal.ReminderEveryMinutes, goal.ActiveFrom, goal.ActiveUntil, goal.ActiveDays, null, null, Measure.Duration, null));

        vm.Title.Should().Be("Estudar alemão");
    }

    [Fact]
    public async Task Open_shows_the_main_window()
    {
        var vm = await RunningAsync();

        vm.OpenCommand.Execute(null);

        _window.Activations.Should().Be(1);
    }

    [Fact]
    public async Task Stop_freezes_the_timer_opens_the_window_and_selects_today()
    {
        var vm = await RunningAsync();
        _shell.CurrentPage = _goals;
        _f.Time.Advance(TimeSpan.FromMinutes(3));

        await vm.StopCommand.ExecuteAsync(null);

        (await _f.Timers.GetAsync())!.StoppedAt.Should().Be(TimerFixture.T0.AddMinutes(3));
        _window.Activations.Should().Be(1);
        _shell.CurrentPage.Should().BeSameAs(_today);
    }

    [Fact]
    public async Task Stop_twice_or_with_the_timer_already_stopped_shows_no_error()
    {
        var vm = await RunningAsync();

        await vm.StopCommand.ExecuteAsync(null);
        await vm.StopCommand.ExecuteAsync(null);
        await _f.Timers.DiscardAsync();
        var act = () => vm.StopCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        _window.Activations.Should().Be(3);
        _shell.CurrentPage.Should().BeSameAs(_today);
    }

    private sealed class FakeActivator : IWindowActivator
    {
        public int Activations { get; private set; }
        public void Activate() => Activations++;
    }

    private sealed class StubPage(string title) : PageViewModel
    {
        public override string Title => title;
    }
}
