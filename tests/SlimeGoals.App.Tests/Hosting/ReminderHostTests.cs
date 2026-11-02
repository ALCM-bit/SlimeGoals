using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Hosting;

public class ReminderHostTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeScheduler _scheduler = new();
    private readonly FakeWindow _window = new();

    public ReminderHostTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private CheckinService Checkins => new(_goals, _checkins, _time);
    private TimerService Timers => new(_goals, _timers, Checkins, _time);
    private ReminderService Reminders => new(_goals, _checkins, _timers, _log, _notifications, new ReminderFormatter(), _time);

    private ReminderHost NewHost() => new(_scheduler, _notifications,
        new ReminderResponseHandler(_log, _goals, Checkins, Timers, _time), Reminders, _window);

    private Task<Goal> Create(string title) => Goals.CreateAsync(new GoalDraft(title, Recurrence.Daily, 60, "book",
        GoalColor.Purple, 60, new TimeOnly(8, 0), new TimeOnly(20, 0), WeekDays.All, null, null, Measure.Duration, null));

    private async Task<ReminderLogEntry> Fire(Goal goal)
    {
        var entry = new ReminderLogEntry(Guid.NewGuid(), goal.Id, Now);
        await _log.AddAsync(entry);
        return entry;
    }

    [Fact]
    public async Task Host_start_starts_scheduler()
    {
        await NewHost().StartAsync();

        _scheduler.Started.Should().Be(1);
    }

    [Fact]
    public async Task Host_open_app_outcome_activates_window()
    {
        var other = await Create("Outra");
        var goal = await Create("Estudar");
        await Timers.StartAsync(other.Id);
        var entry = await Fire(goal);
        var host = NewHost();
        await host.StartAsync();

        _notifications.RaiseAction(entry.Id, ReminderAction.Primary);

        await _window.Activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _window.Count.Should().Be(1);
        (await _log.GetAsync(entry.Id))!.Response.Should().BeNull();
    }

    [Fact]
    public async Task Host_open_action_activates_window_and_handled_does_not()
    {
        var goal = await Create("Estudar");
        var entry = await Fire(goal);
        var host = NewHost();
        await host.StartAsync();

        _notifications.RaiseAction(entry.Id, ReminderAction.Snooze);
        _notifications.RaiseAction(entry.Id, ReminderAction.Open);

        await _window.Activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _window.Count.Should().Be(1);
        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Snooze);
    }

    [Fact]
    public async Task Host_stop_expires_pending()
    {
        var goal = await Create("Estudar");
        var entry = await Fire(goal);
        var host = NewHost();
        await host.StartAsync();

        await host.StopAsync();

        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Ignore);
        _notifications.Withdrawn.Should().Contain(entry.Id);
    }

    [Fact]
    public async Task Host_start_survives_scheduler_failure()
    {
        _scheduler.FailWith = new IOException("banco indisponível");

        await NewHost().Invoking(h => h.StartAsync()).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Host_action_handler_failure_does_not_escape()
    {
        var goal = await Create("Estudar");
        var entry = await Fire(goal);
        await NewHost().StartAsync();
        _window.FailWith = new InvalidOperationException("janela já fechada");

        var raise = () => _notifications.RaiseAction(entry.Id, ReminderAction.Open);

        raise.Should().NotThrow();
        await _window.Activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class FakeScheduler : IReminderScheduler
    {
        public int Started { get; private set; }
        public Exception? FailWith { get; set; }

        public Task StartAsync(CancellationToken ct = default)
        {
            Started++;
            return FailWith is null ? Task.CompletedTask : Task.FromException(FailWith);
        }

        public Task RescheduleAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class FakeWindow : IWindowActivator
    {
        public int Count;
        public TaskCompletionSource Activated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? FailWith { get; set; }

        public void Activate()
        {
            Interlocked.Increment(ref Count);
            Activated.TrySetResult();
            if (FailWith is not null)
                throw FailWith;
        }
    }
}
