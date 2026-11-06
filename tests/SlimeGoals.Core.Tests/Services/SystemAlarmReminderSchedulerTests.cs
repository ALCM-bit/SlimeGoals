using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class SystemAlarmReminderSchedulerTests : IDisposable
{
    // Quarta, 04/11/2026, em São Paulo (UTC-3): hora local → UTC.
    private static DateTimeOffset At(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 11, 4, hour, minute, 0, TimeSpan.Zero).AddHours(3);

    private readonly FakeTimeProvider _time = new(At(7));
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly InMemorySettingsRepository _settings = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly FakeSystemAlarm _alarm = new();
    private SystemAlarmReminderScheduler? _scheduler;

    public SystemAlarmReminderSchedulerTests() =>
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    public void Dispose() => _scheduler?.Dispose();

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, $"{periodTotal}/{goal.Target}", "Feito!");
    }

    private sealed class FakeSystemAlarm : ISystemAlarm
    {
        private int _sets;
        private int _cancels;
        private readonly object _lock = new();
        private DateTimeOffset? _armedFor;

        public DateTimeOffset? ArmedFor { get { lock (_lock) return _armedFor; } }
        public int Sets => Volatile.Read(ref _sets);
        public int Cancels => Volatile.Read(ref _cancels);

        public void Set(DateTimeOffset at)
        {
            lock (_lock) _armedFor = at;
            Interlocked.Increment(ref _sets);
        }

        public void Cancel()
        {
            lock (_lock) _armedFor = null;
            Interlocked.Increment(ref _cancels);
        }
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

    private SystemAlarmReminderScheduler Create(IGoalRepository? goals = null)
    {
        var service = new ReminderService(goals ?? _goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time,
            new ReminderPause(_settings, _time, _changes));
        return _scheduler = new SystemAlarmReminderScheduler(service, _alarm, _changes, _time);
    }

    private async Task<Goal> AddGoal()
    {
        var goal = new Goal
        {
            Id = Guid.NewGuid(), Title = "Meta", Recurrence = Recurrence.Daily, Target = 5, Icon = "book",
            Color = GoalColor.Purple, ReminderEveryMinutes = 60,
            ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(22, 0), ActiveDays = WeekDays.All,
            CreatedAt = At(0), UpdatedAt = At(0),
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

    [Fact]
    public async Task Start_arms_alarm_for_next_reminder()
    {
        await AddGoal();

        await Create().StartAsync();

        _alarm.ArmedFor.Should().Be(At(8));
    }

    [Fact]
    public async Task Start_does_not_expire_pending()
    {
        var goal = await AddGoal();
        var entry = new ReminderLogEntry(Guid.NewGuid(), goal.Id, At(8));
        await _log.AddAsync(entry);
        _time.SetUtcNow(At(8, 30));

        await Create().StartAsync();

        (await _log.GetAsync(entry.Id))!.Response.Should().BeNull();
        _notifications.Withdrawn.Should().BeEmpty();
    }

    [Fact]
    public async Task Fired_shows_due_reminder_arms_next_and_notifies()
    {
        await AddGoal();
        var scheduler = Create();
        await scheduler.StartAsync();
        var notified = 0;
        _changes.Changed += (_, _) => Interlocked.Increment(ref notified);
        _time.SetUtcNow(At(8));

        await scheduler.FiredAsync();

        _notifications.Shown.Should().ContainSingle();
        _alarm.ArmedFor.Should().Be(At(9));
        await Task.Delay(200); // o Changed reagenda; não pode disparar de novo nem avisar em laço
        _notifications.Shown.Should().HaveCount(1);
        Volatile.Read(ref notified).Should().Be(1);
    }

    [Fact]
    public async Task Without_upcoming_reminder_cancels_alarm()
    {
        await Create().StartAsync();

        _alarm.Cancels.Should().BeGreaterThan(0);
        _alarm.ArmedFor.Should().BeNull();
    }

    [Fact]
    public async Task Pause_withdraws_pending_and_cancels_alarm()
    {
        await AddGoal();
        _time.SetUtcNow(At(8));
        await Create().StartAsync();
        var pending = _notifications.Shown.Should().ContainSingle().Subject.LogId;
        _alarm.ArmedFor.Should().Be(At(9));

        await new ReminderPause(_settings, _time, _changes).PauseAsync();

        await Eventually(() => _alarm.ArmedFor is null);
        _notifications.Withdrawn.Should().Contain(pending);
        _log.All.Should().ContainSingle().Which.Response.Should().Be(ReminderResponse.Ignore);
    }

    [Fact]
    public async Task Data_change_rearms()
    {
        await Create().StartAsync();
        _alarm.ArmedFor.Should().BeNull();

        var draft = new GoalDraft("Meta", Recurrence.Daily, 5, "book", GoalColor.Purple, 60,
            new TimeOnly(8, 0), new TimeOnly(22, 0), WeekDays.All, null, null);
        await new GoalService(_goals, _checkins, _timers, _time, _changes).CreateAsync(draft);

        await Eventually(() => _alarm.ArmedFor is not null);
    }

    [Fact]
    public async Task Sync_failure_arms_retry()
    {
        var scheduler = Create(new ThrowingGoalRepository());

        await scheduler.StartAsync();

        _alarm.ArmedFor.Should().Be(_time.GetUtcNow() + SystemAlarmReminderScheduler.RetryAfterFailure);
    }

    [Fact]
    public async Task Start_twice_subscribes_once()
    {
        var scheduler = Create();
        await scheduler.StartAsync();
        await scheduler.StartAsync();
        var before = _alarm.Sets + _alarm.Cancels;

        _changes.NotifyChanged();

        await Eventually(() => _alarm.Sets + _alarm.Cancels > before);
        await Task.Delay(200);
        (_alarm.Sets + _alarm.Cancels).Should().Be(before + 1);
    }
}
