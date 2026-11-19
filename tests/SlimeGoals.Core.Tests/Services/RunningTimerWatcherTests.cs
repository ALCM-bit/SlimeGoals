using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

/// <summary>A sessão de Pomodoro fora do app: sem cronômetro rodando, a janelinha e a notificação mostram a sessão.</summary>
public sealed class RunningTimerWatcherTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers;
    private readonly InMemoryFocusSessionRepository _sessions;
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly RunningTimerWatcher _watcher;

    public RunningTimerWatcherTests()
    {
        _timers = new(_checkins);
        _sessions = new(_checkins, _timers);
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _watcher = new RunningTimerWatcher(Timers, _goals, _changes, Focus);
    }

    public void Dispose() => _watcher.Dispose();

    private CheckinService Checkins => new(_goals, _checkins, _time);
    private TimerService Timers => new(_goals, _timers, Checkins, _time, _changes, _sessions);
    private FocusService Focus => new(_goals, _sessions, _timers, Checkins, new AppSettings(_settings), _time, _changes);

    private Task<Goal> Study() => new GoalService(_goals, _checkins, _timers, _time).CreateAsync(GoalValidatorTests.Valid() with
    {
        Title = "Estudar",
        Icon = "book",
        Color = GoalColor.Green,
        Measure = Measure.Duration,
        Unit = null,
        Recurrence = Recurrence.Daily,
        Target = 60,
        ActiveDays = WeekDays.All,
    });

    [Fact]
    public async Task Watcher_shows_the_session_when_no_timer_runs()
    {
        await Focus.StartAsync(FocusPlan.Default, null);

        await _watcher.StartAsync();

        _watcher.Current.Should().Be(new RunningTimer(Guid.Empty, "Pomodoro", "NavTime", GoalColor.Purple, T0, null,
            new FocusDisplay("Foco", T0.AddMinutes(25), null, T0)));
        _watcher.Current!.Label.Should().Be("Foco");
    }

    [Fact]
    public async Task Label_joins_phase_goal_and_task()
    {
        var goal = await Study();
        await Focus.StartAsync(FocusPlan.Default, goal.Id);
        await Focus.SetTaskAsync("Revisar capítulo 3");

        await _watcher.StartAsync();

        var current = _watcher.Current!;
        (current.GoalId, current.Title, current.Icon, current.Color).Should().Be((goal.Id, "Estudar", "book", GoalColor.Green));
        current.Label.Should().Be("Foco · Estudar · Revisar capítulo 3");
    }

    [Fact]
    public void Label_skips_the_missing_goal_and_task()
    {
        var display = new FocusDisplay("Pausa", T0, null, T0);

        new RunningTimer(Guid.Empty, "Pomodoro", "NavTime", GoalColor.Purple, T0, "Ler", display).Label
            .Should().Be("Pausa · Ler");
        new RunningTimer(Guid.NewGuid(), "Estudar", "book", GoalColor.Green, T0, null, display).Label
            .Should().Be("Pausa · Estudar");
    }

    [Fact]
    public async Task Break_shows_the_break_without_the_task()
    {
        await Focus.StartAsync(FocusPlan.Default, null);
        await Focus.SetTaskAsync("Revisar capítulo 3");
        _time.Advance(TimeSpan.FromMinutes(25));
        await Focus.AdvanceAsync();

        await _watcher.StartAsync();

        _watcher.Current!.Label.Should().Be("Pausa");
        _watcher.Current.Focus.Should().Be(new FocusDisplay("Pausa", T0.AddMinutes(30), null, T0));
        _watcher.Current.StartedAt.Should().Be(T0.AddMinutes(25));
    }

    [Fact]
    public async Task Paused_session_shows_the_frozen_remaining_time()
    {
        await Focus.StartAsync(FocusPlan.Default, null);
        await _watcher.StartAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await Focus.PauseAsync();
        await _watcher.RefreshAsync();

        _watcher.Current!.Focus.Should().Be(new FocusDisplay("Pausado", T0.AddMinutes(25), TimeSpan.FromMinutes(15), T0));
    }

    [Theory]
    [InlineData(0, "Foco concluído")]
    [InlineData(1, "Pausa concluída")]
    public async Task Alerted_block_waits_for_the_tap(int step, string phase)
    {
        await Focus.StartAsync(FocusPlan.Default, null);
        for (var i = 0; i < step; i++)
        {
            _time.Advance(TimeSpan.FromMinutes(25));
            await Focus.AdvanceAsync();
        }
        await _sessions.MarkAlertedAsync(step);

        await _watcher.StartAsync();

        _watcher.Current!.Focus!.Phase.Should().Be(phase);
    }

    [Fact]
    public async Task Finished_session_is_not_running()
    {
        await Focus.StartAsync(new FocusPlan([new FocusBlock(FocusBlockKind.Focus, 1)], 1), null);
        await _watcher.StartAsync();
        _watcher.Current.Should().NotBeNull();
        _time.Advance(TimeSpan.FromMinutes(1));

        await Focus.AdvanceAsync(); // sem meta: fica terminada à espera de um destino; o aviso de mudança relê

        (await _sessions.GetAsync())!.FinishedAt.Should().NotBeNull();
        _watcher.Current.Should().BeNull();
    }

    [Fact]
    public async Task Archived_goal_shows_the_session_as_without_goal()
    {
        var goal = await Study();
        await Focus.StartAsync(FocusPlan.Default, goal.Id);
        await _goals.UpdateAsync((await _goals.GetAsync(goal.Id))! with { ArchivedAt = T0 });

        await _watcher.StartAsync();

        _watcher.Current!.GoalId.Should().Be(Guid.Empty);
        _watcher.Current.Title.Should().Be("Pomodoro");
    }

    [Fact]
    public async Task Without_focus_service_the_watcher_ignores_the_session()
    {
        await Focus.StartAsync(FocusPlan.Default, null);
        using var watcher = new RunningTimerWatcher(Timers, _goals, _changes);

        await watcher.StartAsync();

        watcher.Current.Should().BeNull();
    }
}
