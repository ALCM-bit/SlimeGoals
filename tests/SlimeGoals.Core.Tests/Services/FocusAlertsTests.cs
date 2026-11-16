using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

/// <summary>O aviso de fim de bloco: o alarme armado para o fim do bloco, e um aviso só por passo.</summary>
public class FocusAlertsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers;
    private readonly InMemoryFocusSessionRepository _sessions;
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly FakeAlarm _alarm = new();
    private readonly FakeAlert _alert = new();

    public FocusAlertsTests()
    {
        _timers = new(_checkins);
        _sessions = new(_checkins, _timers);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
    }

    private FocusService Service => new(_goals, _sessions, _timers, new CheckinService(_goals, _checkins, _time),
        new AppSettings(_settings), _time, _changes);

    private FocusAlerts NewAlerts(IFocusAlarm? alarm = null, IFocusAlert? alert = null) =>
        new(Service, _sessions, alarm ?? _alarm, alert ?? _alert, _time, _changes);

    private static FocusBlock Focus(int minutes) => new(FocusBlockKind.Focus, minutes);

    [Fact]
    public async Task Running_focus_arms_the_alarm_for_the_block_end()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();

        await alerts.StartAsync();

        _alarm.ArmedAt.Should().Be(T0.AddMinutes(25));
    }

    [Fact]
    public async Task Starting_a_session_later_arms_through_the_change_notice()
    {
        await NewAlerts().StartAsync();
        _alarm.ArmedAt.Should().BeNull();

        await Service.StartAsync(FocusPlan.Default, null);

        _alarm.ArmedAt.Should().Be(T0.AddMinutes(25));
    }

    [Fact]
    public async Task Paused_session_cancels_the_alarm()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        await NewAlerts().StartAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await Service.PauseAsync(); // o aviso de mudança faz a sincronização

        _alarm.ArmedAt.Should().BeNull();
        _alarm.Cancels.Should().BeGreaterThan(0);

        _time.Advance(TimeSpan.FromMinutes(3));
        await Service.ResumeAsync();

        _alarm.ArmedAt.Should().Be(T0.AddMinutes(28));
    }

    [Fact]
    public async Task Fire_shows_break_time_after_a_focus_and_focus_time_after_a_break()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(25));

        await alerts.FireAsync();

        _alert.Shown.Should().Equal(FocusAlertKind.BreakTime);
        (await _sessions.GetAsync())!.AlertedStep.Should().Be(0);
        _alarm.ArmedAt.Should().BeNull(); // já avisado: nada armado até o toque

        await Service.AdvanceAsync();
        _alarm.ArmedAt.Should().Be(T0.AddMinutes(30));
        _time.Advance(TimeSpan.FromMinutes(5));

        await alerts.FireAsync();

        _alert.Shown.Should().Equal(FocusAlertKind.BreakTime, FocusAlertKind.FocusTime);
    }

    [Fact]
    public async Task Fire_on_the_last_block_shows_session_done()
    {
        await Service.StartAsync(new FocusPlan([Focus(1)], 1), null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        await alerts.FireAsync();

        _alert.Shown.Should().Equal(FocusAlertKind.SessionDone);
    }

    [Fact]
    public async Task Fire_after_restart_alerts_once_per_step()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var first = NewAlerts();
        await first.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(26));
        await first.FireAsync();
        first.Dispose();

        var second = NewAlerts();
        await second.StartAsync();
        await second.FireAsync();

        _alert.Shown.Should().ContainSingle().Which.Should().Be(FocusAlertKind.BreakTime);
        _alert.Withdraws.Should().Be(0); // o aviso do passo atual fica na central
        _alarm.ArmedAt.Should().BeNull();
    }

    [Fact]
    public async Task Early_fire_does_nothing()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromSeconds(25 * 60 - 2));

        await alerts.FireAsync();

        _alert.Shown.Should().BeEmpty();
        (await _sessions.GetAsync())!.AlertedStep.Should().BeNull();
        _alarm.ArmedAt.Should().Be(T0.AddMinutes(25));
    }

    [Fact]
    public async Task Fire_tolerates_one_second_early()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromSeconds(25 * 60 - 1));

        await alerts.FireAsync();

        _alert.Shown.Should().Equal(FocusAlertKind.BreakTime);
    }

    [Fact]
    public async Task Paused_session_never_fires()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(10));
        await Service.PauseAsync();
        _time.Advance(TimeSpan.FromMinutes(30));

        await alerts.FireAsync();

        _alert.Shown.Should().BeEmpty();
        _alarm.ArmedAt.Should().BeNull();
    }

    [Fact]
    public async Task Advancing_withdraws_the_alert()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(25));
        await alerts.FireAsync();
        _alert.Withdraws.Should().Be(0);

        await Service.AdvanceAsync();

        _alert.Withdraws.Should().Be(1);
        await alerts.SyncAsync();
        _alert.Withdraws.Should().Be(1); // retirar uma vez basta
    }

    [Fact]
    public async Task Stale_alert_from_before_a_restart_is_withdrawn()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var first = NewAlerts();
        await first.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(25));
        await first.FireAsync();
        first.Dispose();
        await Service.AdvanceAsync(); // o toque veio de outro lugar, com este processo fora do ar

        await NewAlerts().StartAsync();

        _alert.Withdraws.Should().Be(1);
        _alarm.ArmedAt.Should().Be(T0.AddMinutes(30));
    }

    [Fact]
    public async Task Ending_the_session_cancels_the_alarm_and_withdraws()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(25));
        await alerts.FireAsync();

        await Service.EndAsync(); // sem meta, a sessão fica terminada à espera de um destino

        (await _sessions.GetAsync())!.FinishedAt.Should().NotBeNull();
        _alarm.ArmedAt.Should().BeNull();
        _alert.Withdraws.Should().Be(1);

        await Service.DiscardAsync();

        _alarm.ArmedAt.Should().BeNull();
        _alert.Withdraws.Should().Be(1);
    }

    [Fact]
    public async Task Fire_announces_the_change_so_the_mini_window_shows_the_end()
    {
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts();
        await alerts.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(25));
        var notified = 0;
        _changes.Changed += (_, _) => notified++;

        await alerts.FireAsync();

        notified.Should().Be(1);
    }

    [Theory]
    [InlineData(FocusAlertKind.BreakTime, "Hora da pausa")]
    [InlineData(FocusAlertKind.FocusTime, "Hora de voltar ao foco")]
    [InlineData(FocusAlertKind.SessionDone, "Sessão concluída")]
    public void Alert_texts(FocusAlertKind kind, string text) => FocusAlertText.Of(kind).Should().Be(text);

    [Fact]
    public void Silence_is_passed_to_the_alert()
    {
        var alerts = NewAlerts();

        alerts.Silence();

        _alert.Silences.Should().Be(1);
    }

    [Fact]
    public async Task Failures_go_to_the_trace_without_the_message()
    {
        using var trace = new TraceCapture();
        await Service.StartAsync(FocusPlan.Default, null);
        var alerts = NewAlerts(new BrokenAlarm(), new BrokenAlert());
        _time.Advance(TimeSpan.FromMinutes(25));

        await alerts.StartAsync();
        await alerts.FireAsync();
        alerts.Silence();

        trace.Messages.Should().Contain(m => m.Contains(nameof(InvalidOperationException)));
        trace.Messages.Should().NotContain(m => m.Contains("segredo"));
    }

    private sealed class FakeAlarm : IFocusAlarm
    {
        public DateTimeOffset? ArmedAt { get; private set; }
        public int Cancels { get; private set; }

        public void Set(DateTimeOffset at) => ArmedAt = at;

        public void Cancel()
        {
            ArmedAt = null;
            Cancels++;
        }
    }

    private sealed class FakeAlert : IFocusAlert
    {
        public List<FocusAlertKind> Shown { get; } = [];
        public int Silences { get; private set; }
        public int Withdraws { get; private set; }

        public void Show(FocusAlertKind kind) => Shown.Add(kind);
        public void Silence() => Silences++;
        public void Withdraw() => Withdraws++;
    }

    private sealed class BrokenAlarm : IFocusAlarm
    {
        public void Set(DateTimeOffset at) => throw new InvalidOperationException("segredo");
        public void Cancel() => throw new InvalidOperationException("segredo");
    }

    private sealed class BrokenAlert : IFocusAlert
    {
        public void Show(FocusAlertKind kind) => throw new InvalidOperationException("segredo");
        public void Silence() => throw new InvalidOperationException("segredo");
        public void Withdraw() => throw new InvalidOperationException("segredo");
    }
}
