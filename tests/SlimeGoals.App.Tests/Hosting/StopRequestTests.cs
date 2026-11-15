using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Support;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Tests.Hosting;

public sealed class StopRequestTests : IDisposable
{
    private readonly TimerFixture _f = new();
    private readonly StubPage _today = new("Hoje");
    private readonly StubPage _goals = new("Metas");
    private readonly ShellViewModel _shell;

    public StopRequestTests()
    {
        _shell = new ShellViewModel([_today, _goals]);
        _shell.CurrentPage = _goals;
    }

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task Stop_request_stops_the_timer_and_selects_today()
    {
        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        _f.Time.Advance(TimeSpan.FromMinutes(25));

        await StopRequest.RunAsync(_f.Timers, _shell);

        (await _f.Timers.GetAsync())!.StoppedAt.Should().Be(TimerFixture.T0.AddMinutes(25));
        _shell.CurrentPage.Should().BeSameAs(_today);
    }

    [Fact]
    public async Task Stop_request_with_no_timer_or_an_already_stopped_one_only_selects_today()
    {
        await StopRequest.RunAsync(_f.Timers, _shell);
        _shell.CurrentPage.Should().BeSameAs(_today);

        var goal = await _f.StudyAsync();
        await _f.Timers.StartAsync(goal.Id);
        await _f.Timers.StopAsync();
        _f.Time.Advance(TimeSpan.FromMinutes(10));
        _shell.CurrentPage = _goals;

        await StopRequest.RunAsync(_f.Timers, _shell);

        (await _f.Timers.GetAsync())!.StoppedAt.Should().Be(TimerFixture.T0, "a parada já gravada não muda");
        _shell.CurrentPage.Should().BeSameAs(_today);
    }

    private sealed class StubPage(string title) : PageViewModel
    {
        public override string Title => title;
    }
}
