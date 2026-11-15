using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public sealed class TimerNotificationSyncTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly RecordingNotification _notification = new();
    private readonly RunningTimerWatcher _watcher;
    private readonly TimerService _timerService;

    public TimerNotificationSyncTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _timerService = new TimerService(_goals, _timers, new CheckinService(_goals, _checkins, _time), _time, _changes);
        _watcher = new RunningTimerWatcher(_timerService, _goals, _changes);
    }

    public void Dispose() => _watcher.Dispose();

    private Task<Goal> Study() => new GoalService(_goals, _checkins, _timers, _time).CreateAsync(GoalValidatorTests.Valid() with
        { Measure = Measure.Duration, Unit = null, Recurrence = Recurrence.Daily, Target = 60, ActiveDays = WeekDays.All,
          ActiveFrom = new TimeOnly(0, 0), ActiveUntil = new TimeOnly(23, 59) });

    [Fact]
    public async Task Sync_shows_when_running_and_cancels_when_not()
    {
        var goal = await Study();
        var sync = new TimerNotificationSync(_watcher, _notification);
        await sync.StartAsync();
        _notification.Calls.Should().Equal("cancel");

        await _timerService.StartAsync(goal.Id);
        await sync.SyncAsync();
        _notification.Calls.Last().Should().Be($"show {goal.Id} {T0:O}");
        _notification.Shown.Should().Be(new RunningTimer(goal.Id, goal.Title, goal.Icon, goal.Color, T0));

        await _timerService.StopAsync();
        await sync.SyncAsync();
        _notification.Calls.Last().Should().Be("cancel");
    }

    [Fact]
    public async Task Sync_follows_the_watcher_without_being_asked()
    {
        var goal = await Study();
        var sync = new TimerNotificationSync(_watcher, _notification);
        await sync.StartAsync();

        await _timerService.StartAsync(goal.Id);
        await WaitUntil(() => _notification.Calls.Last().StartsWith("show"));

        await _timerService.DiscardAsync();
        await WaitUntil(() => _notification.Calls.Last() == "cancel");
    }

    [Fact]
    public async Task Sync_survives_a_notification_that_throws()
    {
        var goal = await Study();
        await _timerService.StartAsync(goal.Id);
        _notification.Throw = true;
        using var trace = new TraceCapture();
        var sync = new TimerNotificationSync(_watcher, _notification);

        await sync.StartAsync();
        await sync.SyncAsync();

        trace.Messages.Should().Contain(m => m.Contains("notificação do cronômetro"));
        trace.Messages.Should().NotContain(m => m.Contains("canal-quebrado"));
        _notification.Throw = false;
        await sync.SyncAsync();
        _notification.Calls.Last().Should().StartWith("show");
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "a notificação devia acompanhar o cronômetro");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingNotification : ITimerNotification
    {
        private readonly Lock _lock = new();
        private readonly List<string> _calls = [];

        public bool Throw { get; set; }
        public RunningTimer? Shown { get; private set; }

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_lock)
                    return _calls.ToList();
            }
        }

        public void Show(RunningTimer timer)
        {
            if (Throw)
                throw new InvalidOperationException("canal-quebrado");
            lock (_lock)
            {
                Shown = timer;
                _calls.Add($"show {timer.GoalId} {timer.StartedAt:O}");
            }
        }

        public void Cancel()
        {
            if (Throw)
                throw new InvalidOperationException("canal-quebrado");
            lock (_lock)
                _calls.Add("cancel");
        }
    }

    [Fact]
    public async Task Task_change_republishes_with_the_new_label()
    {
        var goal = await Study();
        var sync = new TimerNotificationSync(_watcher, _notification);
        await sync.StartAsync();
        await _timerService.StartAsync(goal.Id);
        await sync.SyncAsync();

        await _timerService.SetTaskAsync("Capítulo 3");
        await sync.SyncAsync();

        _notification.Shown!.Label.Should().Be($"{goal.Title} · Capítulo 3");
    }
}
