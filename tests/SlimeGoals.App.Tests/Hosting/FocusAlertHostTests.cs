using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Support;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Tests.Hosting;

public sealed class FocusAlertHostTests : IDisposable
{
    private readonly TimerFixture _f = new();
    private readonly FakeAlarm _alarm = new();
    private readonly FakeAlert _alert = new();
    private readonly FakeActivator _window = new();
    private readonly PageNavigator _navigator = new();
    private readonly List<Type> _navigated = [];
    private readonly List<Action> _dispatched = [];
    private readonly FocusAlerts _alerts;
    private readonly FocusAlertHost _host;

    public FocusAlertHostTests()
    {
        _alerts = new FocusAlerts(_f.Focus, _f.Sessions, _alarm, _alert, _f.Time, _f.Changes);
        _host = new FocusAlertHost(_alerts, _window, _navigator, _dispatched.Add);
        _navigator.Requested += _navigated.Add;
    }

    public void Dispose()
    {
        _alerts.Dispose();
        _f.Dispose();
    }

    [Fact]
    public async Task Start_arms_the_alarm_and_a_fire_shows_the_alert()
    {
        await _f.Focus.StartAsync(FocusPlan.Default, null);

        await _host.StartAsync();
        _alarm.ArmedAt.Should().Be(TimerFixture.T0.AddMinutes(25));

        _f.Time.Advance(TimeSpan.FromMinutes(25));
        await _host.FireAsync();
        _alert.Shown.Should().Equal(FocusAlertKind.BreakTime);
    }

    [Fact]
    public void Opening_the_alert_shows_the_time_tab_on_the_ui_thread()
    {
        _host.Open();
        (_navigated.Count, _window.Activations).Should().Be((0, 0), "nada muda fora da thread da interface");

        _dispatched.Should().ContainSingle().Which.Invoke();

        _navigated.Should().Equal(typeof(TimePageViewModel));
        _window.Activations.Should().Be(1);
        _alert.Silenced.Should().Be(1);
    }

    private sealed class FakeAlarm : IFocusAlarm
    {
        public DateTimeOffset? ArmedAt { get; private set; }
        public void Set(DateTimeOffset at) => ArmedAt = at;
        public void Cancel() => ArmedAt = null;
    }

    private sealed class FakeAlert : IFocusAlert
    {
        public List<FocusAlertKind> Shown { get; } = [];
        public int Silenced { get; private set; }
        public void Show(FocusAlertKind kind) => Shown.Add(kind);
        public void Silence() => Silenced++;
        public void Withdraw() { }
    }

    private sealed class FakeActivator : IWindowActivator
    {
        public int Activations { get; private set; }
        public void Activate() => Activations++;
    }
}
