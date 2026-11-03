using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Services;

public class ReminderServiceTests
{
    // Quarta, 04/11/2026, em São Paulo (UTC-3): hora local → UTC.
    private static DateTimeOffset Today(int hour, int minute = 0) =>
        new DateTimeOffset(2026, 11, 4, hour, minute, 0, TimeSpan.Zero).AddHours(3);

    private readonly FakeTimeProvider _time = new(Today(8));
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();

    public ReminderServiceTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new($"T {goal.Title}", $"B {periodTotal}/{goal.Target}", "Feito!");
    }

    private readonly InMemorySettingsRepository _settings = new();
    private ReminderPause Pause => new(_settings, _time);

    private ReminderService Service => new(_goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time);

    private ReminderService PausableService => new(_goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time, Pause);

    private async Task<Goal> Add(string title = "Meta", int target = 1)
    {
        var goal = G(Recurrence.Daily) with
        {
            Title = title, Target = target, ReminderEveryMinutes = 60,
            ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        };
        await _goals.AddAsync(goal);
        return goal;
    }

    private void At(int hour, int minute = 0) => _time.SetUtcNow(Today(hour, minute));

    [Fact]
    public async Task Sync_fires_due_goal_logs_it_and_returns_next_interval()
    {
        var goal = await Add();

        var next = await Service.SyncAsync();

        _notifications.Shown.Should().ContainSingle().Which.GoalId.Should().Be(goal.Id);
        _log.All.Should().ContainSingle().Which.Should().Match<ReminderLogEntry>(e =>
            e.GoalId == goal.Id && e.FiredAt == Today(8) && e.Response == null && e.Id == _notifications.Shown[0].LogId);
        next.Should().Be(Today(9));
    }

    [Fact]
    public async Task Sync_twice_does_not_fire_twice()
    {
        await Add();
        await Service.SyncAsync();
        await Service.SyncAsync();

        _notifications.Shown.Should().HaveCount(1);
        _log.All.Should().HaveCount(1);
    }

    [Fact]
    public async Task Five_overdue_goals_fire_once_each()
    {
        for (var i = 0; i < 5; i++) await Add($"Meta {i}");
        At(14);

        await Service.SyncAsync();
        _notifications.Shown.Should().HaveCount(5);

        await Service.SyncAsync();
        _notifications.Shown.Should().HaveCount(5);
    }

    [Fact]
    public async Task Next_fire_withdraws_previous_pending_as_ignore()
    {
        await Add();
        await Service.SyncAsync();
        var first = _notifications.Shown[0].LogId;

        At(9);
        await Service.SyncAsync();

        _notifications.Shown.Should().HaveCount(2);
        _notifications.Withdrawn.Should().Equal(first);
        var entry = (await _log.GetAsync(first))!;
        entry.Response.Should().Be(ReminderResponse.Ignore);
        entry.RespondedAt.Should().Be(Today(9));
    }

    [Fact]
    public async Task Met_goal_pending_is_withdrawn_as_ignore()
    {
        var goal = await Add();
        await Service.SyncAsync();
        var first = _notifications.Shown[0].LogId;

        await _checkins.AddAsync(new Checkin { Id = Guid.NewGuid(), GoalId = goal.Id, Amount = 1, DoneAt = Today(8, 30), UpdatedAt = Today(8, 30) });
        At(8, 30);
        await Service.SyncAsync();

        _notifications.Withdrawn.Should().Equal(first);
        (await _log.GetAsync(first))!.Response.Should().Be(ReminderResponse.Ignore);
        _notifications.Shown.Should().HaveCount(1);
    }

    [Fact]
    public async Task Pending_of_deleted_or_archived_goal_is_withdrawn_as_ignore()
    {
        var goal = await Add();
        await Service.SyncAsync();
        await _goals.UpdateAsync(goal with { ArchivedAt = Today(8, 5) });

        await Service.SyncAsync();

        _notifications.Withdrawn.Should().Equal(_notifications.Shown[0].LogId);
        _log.All.Single().Response.Should().Be(ReminderResponse.Ignore);
    }

    // Revisão final da Fase 4: pendente de ontem não pode valer para o novo dia.
    [Fact]
    public async Task Pending_from_yesterday_is_withdrawn_as_ignore_after_midnight()
    {
        await Add(target: 5);
        At(19, 30);
        await Service.SyncAsync();
        var fired = _notifications.Shown.Single().LogId;

        _time.SetUtcNow(Today(0).AddDays(1));
        await Service.SyncAsync();

        _log.All.Single(e => e.Id == fired).Response.Should().Be(ReminderResponse.Ignore);
        _notifications.Withdrawn.Should().Contain(fired);
    }

    [Fact]
    public async Task Failing_notification_still_logs_and_advances()
    {
        await Add();
        _notifications.ShowFailsWith = new InvalidOperationException("dbus");

        var next = await Service.SyncAsync();

        _log.All.Should().HaveCount(1);
        next.Should().Be(Today(9));
    }

    [Fact]
    public async Task Upcoming_today_lists_only_today_sorted()
    {
        var late = await Add("Tarde");
        await _goals.UpdateAsync(late with { ActiveFrom = new TimeOnly(15, 0) });
        var early = await Add("Cedo");
        await _goals.UpdateAsync(early with { ActiveFrom = new TimeOnly(10, 0) });
        await _goals.AddAsync(G(Recurrence.Daily, days: WeekDays.Saturday) with { Title = "Sábado" });

        var upcoming = await Service.UpcomingTodayAsync();

        upcoming.Select(u => u.Goal.Title).Should().Equal("Cedo", "Tarde");
        upcoming.Select(u => u.At).Should().Equal(Today(10), Today(15));
    }

    [Fact]
    public async Task Expire_pending_marks_ignore_and_withdraws()
    {
        await Add();
        await Service.SyncAsync();
        var id = _notifications.Shown[0].LogId;

        await Service.ExpirePendingAsync();

        _notifications.Withdrawn.Should().Equal(id);
        _log.All.Single().Response.Should().Be(ReminderResponse.Ignore);
    }

    [Fact]
    public async Task Formatter_text_is_used()
    {
        await Add("Água", target: 3);

        await Service.SyncAsync();

        _notifications.Shown.Single().Text.Should().Be(new ReminderText("T Água", "B 0/3", "Feito!"));
    }

    [Fact]
    public async Task Goal_created_now_is_not_fired_immediately()
    {
        At(14);
        var goal = await Add();
        await _goals.UpdateAsync(goal with { CreatedAt = Today(14) });

        var next = await Service.SyncAsync();

        _notifications.Shown.Should().BeEmpty();
        next.Should().Be(Today(15));
    }

    [Fact]
    public async Task Paused_sync_fires_nothing_withdraws_pending_and_returns_null()
    {
        await Add();
        At(9);
        await PausableService.SyncAsync();
        var pendingId = _notifications.Shown.Should().ContainSingle().Subject.LogId;
        At(9, 30);
        await Pause.PauseAsync();

        var next = await PausableService.SyncAsync();

        next.Should().BeNull();
        _notifications.Withdrawn.Should().Contain(pendingId);
        _log.All.Should().ContainSingle().Which.Response.Should().Be(ReminderResponse.Ignore);
        At(11);
        await PausableService.SyncAsync();
        _notifications.Shown.Should().HaveCount(1);
    }

    [Fact]
    public async Task Paused_upcoming_is_empty()
    {
        await Add();
        await Pause.PauseAsync();

        (await PausableService.UpcomingTodayAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Resume_restarts_interval_from_resume_time()
    {
        await Add();
        At(9);
        await Pause.PauseAsync();
        At(15);
        await Pause.ResumeAsync();

        var next = await PausableService.SyncAsync();

        _notifications.Shown.Should().BeEmpty();
        next.Should().Be(Today(16));
    }

    [Fact]
    public async Task Snooze_uses_configured_minutes()
    {
        await _settings.SetAsync(AppSettings.SnoozeMinutesKey, "30");
        await Add();
        var service = new ReminderService(_goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time,
            settings: new AppSettings(_settings));
        At(10);
        await service.SyncAsync();
        var fired = _notifications.Shown.Should().ContainSingle().Subject.LogId;
        await _log.RespondAsync(fired, ReminderResponse.Snooze, _time.GetUtcNow());

        var next = await service.SyncAsync();

        next.Should().Be(Today(10, 30));
    }

    // Revisão final da Fase 4d: adiar respondido antes da pausa não pode disparar na retomada.
    [Fact]
    public async Task Resume_supersedes_snooze_answered_before_pause()
    {
        await Add();
        await PausableService.SyncAsync();
        var fired = _notifications.Shown.Should().ContainSingle().Subject.LogId;
        At(8, 55);
        await _log.RespondAsync(fired, ReminderResponse.Snooze, _time.GetUtcNow());
        At(9);
        await Pause.PauseAsync();
        await PausableService.SyncAsync();
        At(15);
        await Pause.ResumeAsync();

        var next = await PausableService.SyncAsync();

        _notifications.Shown.Should().ContainSingle();
        next.Should().Be(Today(16));
    }

    [Fact]
    public async Task Resume_from_yesterday_is_not_an_anchor()
    {
        await Add();
        var yesterday = Today(15).AddDays(-1).UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await _settings.SetAsync(ReminderPause.ResumedAtKey, yesterday);
        At(14);

        var next = await PausableService.SyncAsync();

        _notifications.Shown.Should().ContainSingle();
        next.Should().Be(Today(15));
    }

    [Theory]
    [InlineData(10, 12)]
    [InlineData(12, 10)]
    public async Task Later_of_creation_and_resume_is_the_anchor(int createdHour, int resumedHour)
    {
        var goal = await Add();
        await _goals.UpdateAsync(goal with { CreatedAt = Today(createdHour) });
        At(resumedHour);
        await Pause.PauseAsync();
        await Pause.ResumeAsync();
        At(12, 30);

        var next = await PausableService.SyncAsync();

        _notifications.Shown.Should().BeEmpty();
        next.Should().Be(Today(13));
    }
}
