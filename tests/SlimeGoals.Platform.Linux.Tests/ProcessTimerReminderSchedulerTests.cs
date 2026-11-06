using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Platform.Linux.Scheduling;
using SlimeGoals.Testing;

namespace SlimeGoals.Platform.Linux.Tests;

public class ProcessTimerReminderSchedulerTests : IDisposable
{
    // Quarta, 04/11/2026, em São Paulo (UTC-3): hora local → UTC.
    private static DateTimeOffset At(int hour, int day = 4) =>
        new DateTimeOffset(2026, 11, day, hour, 0, 0, TimeSpan.Zero).AddHours(3);

    private readonly FakeTimeProvider _time = new(At(7));
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly DataChangeNotifier _changes = new();
    private ProcessTimerReminderScheduler? _scheduler;

    public ProcessTimerReminderSchedulerTests() =>
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    public void Dispose() => _scheduler?.Dispose();

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, $"{periodTotal}/{goal.Target}", "Feito!");
    }

    private sealed class ThrowingGoalRepository : IGoalRepository
    {
        public Task AddAsync(Goal goal, CancellationToken ct = default) => throw new InvalidOperationException("falha");
        public Task UpdateAsync(Goal goal, CancellationToken ct = default) => throw new InvalidOperationException("falha");
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new InvalidOperationException("falha");
        public Task<Goal?> GetAsync(Guid id, CancellationToken ct = default) => throw new InvalidOperationException("falha");
        public Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default) =>
            throw new InvalidOperationException("falha");
    }

    private ProcessTimerReminderScheduler Create(IGoalRepository? goals = null)
    {
        var service = new ReminderService(goals ?? _goals, _checkins, new InMemoryTimerRepository(), _log,
            _notifications, new FakeFormatter(), _time);
        return _scheduler = new ProcessTimerReminderScheduler(service, _changes, _time);
    }

    private async Task AddGoal(int target = 1)
    {
        await _goals.AddAsync(new Goal
        {
            Id = Guid.NewGuid(), Title = "Meta", Recurrence = Recurrence.Daily, Target = target, Icon = "book",
            Color = GoalColor.Purple, ReminderEveryMinutes = 60,
            ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0), ActiveDays = WeekDays.All,
            CreatedAt = At(0, 1), UpdatedAt = At(0, 1),
        });
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    [Fact]
    public async Task Start_arms_for_next_reminder()
    {
        await AddGoal();
        var scheduler = Create();

        await scheduler.StartAsync();

        scheduler.ArmedFor.Should().Be(At(8));
    }

    [Fact]
    public async Task Timer_fires_shows_notification_and_rearms()
    {
        await AddGoal(target: 5);
        var scheduler = Create();
        await scheduler.StartAsync();

        _time.Advance(TimeSpan.FromHours(1));

        await Eventually(() => scheduler.ArmedFor == At(9));
        _notifications.Shown.Should().HaveCount(1);
    }

    // Revisão da Task 7: a tela Hoje recarrega os "Próximos lembretes" depois de cada disparo.
    [Fact]
    public async Task Reminder_tick_notifies_change_without_firing_again()
    {
        await AddGoal(target: 5);
        var scheduler = Create();
        var notified = 0;
        _changes.Changed += (_, _) => Interlocked.Increment(ref notified);
        await scheduler.StartAsync();

        _time.SetUtcNow(At(8));

        await Eventually(() => scheduler.ArmedFor == At(9) && Volatile.Read(ref notified) >= 1);
        await Task.Delay(200); // o Changed reagenda; reagendar não pode disparar de novo nem avisar em laço
        _notifications.Shown.Should().HaveCount(1);
        scheduler.ArmedFor.Should().Be(At(9));
        Volatile.Read(ref notified).Should().Be(1);
    }

    [Fact]
    public async Task No_goals_arms_for_local_midnight_and_notifies_change_there()
    {
        var scheduler = Create();
        var notified = 0;
        _changes.Changed += (_, _) => Interlocked.Increment(ref notified);
        await scheduler.StartAsync();

        scheduler.ArmedFor.Should().Be(At(0, 5));

        _time.SetUtcNow(At(0, 5));

        await Eventually(() => scheduler.ArmedFor == At(0, 6));
        notified.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Data_change_reschedules()
    {
        await AddGoal();
        var goalId = (await _goals.ListAsync(false)).Single().Id;
        _time.SetUtcNow(At(7));
        var scheduler = Create();
        await scheduler.StartAsync();
        scheduler.ArmedFor.Should().Be(At(8));

        _time.SetUtcNow(At(8).AddMinutes(30));
        await new CheckinService(_goals, _checkins, _time, _changes).AdjustAsync(goalId, 1);

        await Eventually(() => scheduler.ArmedFor == At(0, 5));
    }

    [Fact]
    public async Task Failing_sync_rearms_for_midnight()
    {
        var scheduler = Create(new ThrowingGoalRepository());

        await scheduler.StartAsync();

        scheduler.ArmedFor.Should().Be(At(0, 5));
    }
}
