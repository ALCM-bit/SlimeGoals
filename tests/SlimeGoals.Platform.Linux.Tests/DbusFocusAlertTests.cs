using SlimeGoals.Core.Abstractions;
using SlimeGoals.Platform.Linux.Notifications;
using SlimeGoals.Testing;

namespace SlimeGoals.Platform.Linux.Tests;

public sealed class DbusFocusAlertTests : IDisposable
{
    private readonly FakeNotificationBus _bus = new();
    private readonly DbusFocusAlert _alert;

    public DbusFocusAlertTests() => _alert = new DbusFocusAlert(_bus);

    public void Dispose() => _alert.Dispose();

    [Fact]
    public async Task Alert_is_critical_with_alarm_sound_and_replaces_the_previous()
    {
        _alert.Show(FocusAlertKind.BreakTime);
        _alert.Show(FocusAlertKind.FocusTime);
        await _alert.Pending;

        _bus.Notified.Select(n => (n.Summary, n.Critical, n.SoundName)).Should().Equal(
            ("Hora da pausa", true, "alarm-clock-elapsed"),
            ("Hora de voltar ao foco", true, "alarm-clock-elapsed"));
        _bus.Notified.Should().AllSatisfy(n => n.Actions.Should().Equal(("default", "Abrir")));
        _bus.Log.Should().Equal("notify 1", "close 1", "notify 2");
    }

    [Fact]
    public async Task Withdraw_closes_the_open_alert()
    {
        _alert.Show(FocusAlertKind.SessionDone);
        _alert.Withdraw();
        _alert.Withdraw();
        await _alert.Pending;

        _bus.Notified.Single().Summary.Should().Be("Sessão concluída");
        _bus.Closed.Should().Equal(1u);
    }

    [Fact]
    public async Task Withdraw_without_an_alert_and_silence_send_nothing()
    {
        _alert.Withdraw();
        _alert.Silence();
        await _alert.Pending;

        _bus.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task An_alert_closed_by_the_person_is_not_closed_again()
    {
        _alert.Show(FocusAlertKind.BreakTime);
        await _alert.Pending;
        _bus.RaiseClosed(1, 2);

        _alert.Show(FocusAlertKind.FocusTime);
        _alert.Withdraw();
        await _alert.Pending;

        _bus.Log.Should().Equal("notify 1", "notify 2", "close 2");
    }

    [Fact]
    public async Task Clicking_the_alert_raises_opened_and_other_notifications_do_not()
    {
        var opened = 0;
        _alert.Opened += (_, _) => opened++;
        _alert.Show(FocusAlertKind.BreakTime);
        await _alert.Pending;

        _bus.RaiseAction(99, "default");
        _bus.RaiseAction(1, "other");
        opened.Should().Be(0);

        _bus.RaiseAction(1, "default");
        opened.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_send_goes_to_the_trace_without_the_message_and_the_next_one_works()
    {
        using var trace = new TraceCapture();
        _bus.NotifyFailure = new InvalidOperationException("segredo");
        _alert.Show(FocusAlertKind.BreakTime);
        await _alert.Pending;

        _bus.NotifyFailure = null;
        _alert.Show(FocusAlertKind.FocusTime);
        await _alert.Pending;

        trace.Messages.Should().Contain(m => m.Contains("InvalidOperationException"));
        trace.Messages.Should().NotContain(m => m.Contains("segredo"));
        _bus.Notified.Single().Summary.Should().Be("Hora de voltar ao foco");
    }
}
