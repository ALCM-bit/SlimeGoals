using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Platform.Linux.Scheduling;
using SlimeGoals.Testing;

namespace SlimeGoals.Platform.Linux.Tests;

/// <summary>
/// Fluxo real handler → notificador → agendador → serviço, com um só <see cref="DataChangeNotifier"/>
/// (revisão final da Fase 4).
/// </summary>
public class ReminderFlowIntegrationTests : IDisposable
{
    // Quarta, 04/11/2026, em São Paulo (UTC-3): hora local → UTC.
    private static DateTimeOffset At(int hour, int minute = 0, int day = 4) =>
        new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.Zero).AddHours(3);

    private readonly FakeTimeProvider _time = new(At(7));
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly ProcessTimerReminderScheduler _scheduler;
    private readonly ReminderResponseHandler _handler;

    public ReminderFlowIntegrationTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        var checkins = new CheckinService(_goals, _checkins, _time, _changes);
        var timers = new TimerService(_goals, _timers, checkins, _time, _changes);
        _handler = new ReminderResponseHandler(_log, _goals, checkins, timers, _time, _changes);
        var service = new ReminderService(_goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time);
        _scheduler = new ProcessTimerReminderScheduler(service, _changes, _time);
    }

    public void Dispose() => _scheduler.Dispose();

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, $"{periodTotal}/{goal.Target}", "Feito!");
    }

    private async Task<Goal> AddGoal(Measure measure, int target)
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(), Title = "Meta", Measure = measure, Unit = measure == Measure.Quantity ? "copos" : null,
            Recurrence = Recurrence.Daily, Target = target, Icon = "book", Color = GoalColor.Purple,
            ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
            ActiveDays = WeekDays.All, CreatedAt = At(0, day: 1), UpdatedAt = At(0, day: 1),
        };
        await _goals.AddAsync(goal);
        return goal;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    [Theory]
    [InlineData(Measure.Completion, 1)] // Feito
    [InlineData(Measure.Quantity, 1)] // +1 que atinge o alvo
    [InlineData(Measure.Duration, 60)] // Iniciar
    public async Task Primary_from_notification_is_logged_done_with_scheduler_subscribed(Measure measure, int target)
    {
        var goal = await AddGoal(measure, target);
        await _scheduler.StartAsync();
        _time.SetUtcNow(At(8));
        await Eventually(() => _notifications.Shown.Count == 1 && _scheduler.ArmedFor != At(8));
        var logId = _notifications.Shown.Single().LogId;

        var outcome = await _handler.HandleAsync(logId, ReminderAction.Primary);
        await Task.Delay(100); // deixa o reagendamento disparado pelo Changed terminar

        outcome.Should().Be(ReminderOutcome.Handled);
        (await _log.GetAsync(logId))!.Response.Should().Be(ReminderResponse.Done);
        _notifications.Shown.Where(r => r.GoalId == goal.Id).Should().ContainSingle();
    }

    [Fact]
    public async Task Pending_from_yesterday_is_withdrawn_at_midnight()
    {
        await AddGoal(Measure.Quantity, 5);
        _time.SetUtcNow(At(19, 30));
        await _scheduler.StartAsync();
        var logId = _notifications.Shown.Single().LogId;
        _scheduler.ArmedFor.Should().Be(At(0, day: 5));

        _time.SetUtcNow(At(0, day: 5));

        await Eventually(() => _log.All.Single(e => e.Id == logId).Response is not null);
        (await _log.GetAsync(logId))!.Response.Should().Be(ReminderResponse.Ignore);
        _notifications.Withdrawn.Should().Contain(logId);
    }
}
