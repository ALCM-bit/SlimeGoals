using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;
using static SlimeGoals.Core.Tests.Support.Goals;

namespace SlimeGoals.Core.Tests.Services;

/// <summary>Exclusão definitiva: o que some e o que acontece com as notificações pendentes (spec §9).</summary>
public class GoalDeletionTests
{
    // Quarta, 04/11/2026, 08:00 em São Paulo.
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 11, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals;
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();
    private readonly InMemoryReminderLogRepository _log = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly DataChangeNotifier _changes = new();
    private readonly ReminderService _reminders;
    private readonly GoalService _service;

    public GoalDeletionTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        _goals = new InMemoryGoalRepository(_checkins, _log, _timers);
        _reminders = new ReminderService(_goals, _checkins, _timers, _log, _notifications, new FakeFormatter(), _time);
        _service = new GoalService(_goals, _checkins, _timers, _time, _changes, _reminders);
    }

    private sealed class FakeFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, "corpo", "Feito!");
    }

    private async Task<Goal> Add(string title = "Meta")
    {
        var goal = G(Recurrence.Daily) with
        {
            Title = title, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        };
        await _goals.AddAsync(goal);
        return goal;
    }

    [Fact]
    public async Task Delete_removes_goal_and_everything_linked_to_it()
    {
        var goal = await Add();
        var other = await Add("Outra");
        await _reminders.SyncAsync();
        await _checkins.AddAsync(new Checkin { Id = Guid.NewGuid(), GoalId = goal.Id, Amount = 1, DoneAt = T0, UpdatedAt = T0 });
        await _checkins.AddAsync(new Checkin { Id = Guid.NewGuid(), GoalId = other.Id, Amount = 1, DoneAt = T0, UpdatedAt = T0 });
        await _timers.TryStartAsync(new ActiveTimer(goal.Id, T0));

        await _service.DeleteAsync(goal.Id);

        (await _service.GetAsync(goal.Id)).Should().BeNull();
        _goals.Count.Should().Be(1);
        _checkins.All.Should().ContainSingle().Which.GoalId.Should().Be(other.Id);
        _log.All.Should().ContainSingle().Which.GoalId.Should().Be(other.Id);
        (await _timers.GetAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Delete_of_missing_goal_throws_not_found()
    {
        var act = () => _service.DeleteAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    // A ordem importa: só o log sabe qual notificação é da meta, então os pendentes são lidos antes de apagar.
    [Fact]
    public async Task Delete_withdraws_pending_notifications_of_that_goal_only()
    {
        var goal = await Add();
        var other = await Add("Outra");
        await _reminders.SyncAsync();
        var mine = _notifications.Shown.Single(r => r.GoalId == goal.Id).LogId;
        var theirs = _notifications.Shown.Single(r => r.GoalId == other.Id).LogId;

        await _service.DeleteAsync(goal.Id);

        _notifications.Withdrawn.Should().Equal(mine);
        _notifications.Visible.Should().Equal(theirs);
    }

    [Fact]
    public async Task Answered_reminder_is_not_withdrawn_again()
    {
        var goal = await Add();
        await _reminders.SyncAsync();
        await _log.RespondAsync(_notifications.Shown.Single().LogId, ReminderResponse.Snooze, T0);

        await _service.DeleteAsync(goal.Id);

        _notifications.Withdrawn.Should().BeEmpty();
    }

    // Se o banco recusar, a meta fica: a notificação dela também tem de ficar.
    [Fact]
    public async Task Failed_delete_keeps_the_notification()
    {
        var goal = await Add();
        await _reminders.SyncAsync();
        _goals.FailWritesWith = new InvalidOperationException("disco cheio");

        var act = () => _service.DeleteAsync(goal.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _notifications.Withdrawn.Should().BeEmpty();
        _log.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Failure_to_withdraw_does_not_fail_the_delete()
    {
        var goal = await Add();
        await _reminders.SyncAsync();
        _notifications.WithdrawFailsWith = new InvalidOperationException("D-Bus fora do ar");
        var changed = 0;
        _changes.Changed += (_, _) => changed++;

        await _service.DeleteAsync(goal.Id);

        (await _service.GetAsync(goal.Id)).Should().BeNull();
        changed.Should().Be(1);
    }

    // Disparo em andamento: o log já foi gravado e a notificação ainda não apareceu. Sem esperar por ele,
    // a exclusão retiraria uma notificação que ainda não existe, e ela surgiria depois, órfã.
    [Fact]
    public async Task Delete_during_a_fire_does_not_leave_the_notification_behind()
    {
        var goal = await Add();
        _notifications.ShowStarted = new TaskCompletionSource();
        _notifications.ShowGate = new TaskCompletionSource();
        var sync = _reminders.SyncAsync();
        await _notifications.ShowStarted.Task;

        var delete = _service.DeleteAsync(goal.Id);
        _notifications.ShowGate.SetResult();
        await Task.WhenAll(sync, delete);

        _notifications.Shown.Should().ContainSingle();
        _notifications.Visible.Should().BeEmpty();
        _log.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Next_sync_after_delete_fires_nothing_for_the_goal()
    {
        var goal = await Add();
        await _reminders.SyncAsync();
        await _service.DeleteAsync(goal.Id);

        _time.Advance(TimeSpan.FromHours(2));
        await _reminders.SyncAsync();

        _notifications.Shown.Should().ContainSingle();
        _log.All.Should().BeEmpty();
    }
}
