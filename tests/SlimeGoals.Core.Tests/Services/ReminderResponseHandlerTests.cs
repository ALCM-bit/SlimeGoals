using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class ReminderResponseHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly DataChangeNotifier _changes = new();
    private int _changeCount;

    public ReminderResponseHandlerTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _changes.Changed += (_, _) => _changeCount++;
    }

    private GoalService Goals => new(_goals, _checkins, _timers, _time, _changes);
    private CheckinService Checkins => new(_goals, _checkins, _time, _changes);
    private TimerService Timers => new(_goals, _timers, Checkins, _time, _changes);
    private ReminderResponseHandler Handler => new(_log, _goals, Checkins, Timers, _time, _changes);

    private Task<Goal> Create(Measure measure, string title = "Meta") => Goals.CreateAsync(GoalValidatorTests.Valid() with
    {
        Title = title,
        Measure = measure,
        Unit = measure == Measure.Quantity ? "copos" : null,
        Recurrence = Recurrence.Daily,
        Target = measure == Measure.Completion ? 1 : 60,
        ActiveDays = WeekDays.All,
    });

    private async Task<ReminderLogEntry> Fire(Goal goal)
    {
        var entry = new ReminderLogEntry(Guid.NewGuid(), goal.Id, T0);
        await _log.AddAsync(entry);
        return entry;
    }

    [Theory]
    [InlineData(Measure.Completion)]
    [InlineData(Measure.Quantity)]
    public async Task Primary_on_completion_and_quantity_adds_1_and_logs_done(Measure measure)
    {
        var goal = await Create(measure);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await Checkins.PeriodTotalAsync(goal.Id)).Should().Be(1);
        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Done);
    }

    [Fact]
    public async Task Primary_on_duration_starts_timer_and_logs_done()
    {
        var goal = await Create(Measure.Duration);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await Timers.GetAsync())!.GoalId.Should().Be(goal.Id);
        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Done);
    }

    [Fact]
    public async Task Primary_on_duration_with_other_timer_returns_open_app_and_keeps_pending()
    {
        var other = await Create(Measure.Duration, "Outra");
        var goal = await Create(Measure.Duration, "Estudar");
        await Timers.StartAsync(other.Id);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.OpenApp);

        (await _log.GetAsync(entry.Id))!.Response.Should().BeNull();
        (await Timers.GetAsync())!.GoalId.Should().Be(other.Id);
    }

    [Fact]
    public async Task Primary_on_duration_with_orphan_timer_starts_normally()
    {
        var other = await Create(Measure.Duration, "Outra");
        var goal = await Create(Measure.Duration, "Estudar");
        await Timers.StartAsync(other.Id);
        await _goals.UpdateAsync((await _goals.GetAsync(other.Id))! with { ArchivedAt = T0 });
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await Timers.GetAsync())!.GoalId.Should().Be(goal.Id);
    }

    [Fact]
    public async Task Primary_on_already_completed_goal_logs_ignore()
    {
        var goal = await Create(Measure.Completion);
        await Checkins.AdjustAsync(goal.Id, 1);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Ignore);
        (await Checkins.PeriodTotalAsync(goal.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Primary_on_missing_goal_logs_ignore()
    {
        var goal = await Create(Measure.Completion);
        var entry = await Fire(goal);
        await Goals.DeleteAsync(goal.Id);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Ignore);
    }

    [Fact]
    public async Task Second_click_is_stale_and_records_nothing()
    {
        var goal = await Create(Measure.Quantity);
        var entry = await Fire(goal);

        await Handler.HandleAsync(entry.Id, ReminderAction.Primary);
        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Stale);

        (await Checkins.PeriodTotalAsync(goal.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Unknown_log_id_is_stale()
    {
        (await Handler.HandleAsync(Guid.NewGuid(), ReminderAction.Snooze)).Should().Be(ReminderOutcome.Stale);
    }

    [Theory]
    [InlineData(ReminderAction.Snooze, ReminderResponse.Snooze)]
    [InlineData(ReminderAction.Ignore, ReminderResponse.Ignore)]
    public async Task Snooze_and_ignore_are_logged_with_time(ReminderAction action, ReminderResponse expected)
    {
        var goal = await Create(Measure.Quantity);
        var entry = await Fire(goal);
        _time.Advance(TimeSpan.FromMinutes(3));

        (await Handler.HandleAsync(entry.Id, action)).Should().Be(ReminderOutcome.Handled);

        var saved = (await _log.GetAsync(entry.Id))!;
        saved.Response.Should().Be(expected);
        saved.RespondedAt.Should().Be(T0.AddMinutes(3));
    }

    [Fact]
    public async Task Open_returns_open_app_without_logging()
    {
        var goal = await Create(Measure.Quantity);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Open)).Should().Be(ReminderOutcome.OpenApp);

        (await _log.GetAsync(entry.Id))!.Response.Should().BeNull();
    }

    [Fact]
    public async Task Handler_and_services_notify_changes()
    {
        var goal = await Create(Measure.Quantity);
        _changeCount.Should().BeGreaterThan(0); // CreateAsync
        var duration = await Create(Measure.Duration);

        var before = _changeCount;
        await Checkins.AdjustAsync(goal.Id, 1);
        _changeCount.Should().BeGreaterThan(before);

        before = _changeCount;
        await Timers.StartAsync(duration.Id);
        _changeCount.Should().BeGreaterThan(before);

        var entry = await Fire(goal);
        before = _changeCount;
        await Handler.HandleAsync(entry.Id, ReminderAction.Ignore);
        _changeCount.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task Concurrent_primary_on_same_log_records_once()
    {
        var goal = await Create(Measure.Quantity);
        var entry = await Fire(goal);
        var handler = Handler;

        var outcomes = await Task.WhenAll(
            Task.Run(() => handler.HandleAsync(entry.Id, ReminderAction.Primary)),
            Task.Run(() => handler.HandleAsync(entry.Id, ReminderAction.Primary)));

        outcomes.Should().BeEquivalentTo([ReminderOutcome.Handled, ReminderOutcome.Stale]);
        (await Checkins.PeriodTotalAsync(goal.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Primary_on_duration_with_same_goal_timer_running_logs_ignore()
    {
        var goal = await Create(Measure.Duration);
        await Timers.StartAsync(goal.Id);
        var entry = await Fire(goal);

        (await Handler.HandleAsync(entry.Id, ReminderAction.Primary)).Should().Be(ReminderOutcome.Handled);

        (await _log.GetAsync(entry.Id))!.Response.Should().Be(ReminderResponse.Ignore);
    }
}
