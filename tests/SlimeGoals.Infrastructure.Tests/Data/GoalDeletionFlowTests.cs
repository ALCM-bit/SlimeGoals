using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>
/// Exclusão com notificação na central, de ponta a ponta: mesmo registro no container que as raízes de composição
/// (é ele que entrega o <see cref="ReminderService"/> ao <see cref="GoalService"/>) e SQLite de verdade.
/// </summary>
public sealed class GoalDeletionFlowTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"slimegoals-flow-{Guid.NewGuid():N}");
    private readonly FakeNotifications _notifications = new();
    private readonly ServiceProvider _services;

    public GoalDeletionFlowTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(T0));
        services.AddSingleton<DataChangeNotifier>();
        services.AddSingleton<GoalService>();
        services.AddSingleton<CheckinService>();
        services.AddSingleton<TimerService>();
        services.AddSingleton<ReminderService>();
        services.AddSingleton<ReminderResponseHandler>();
        services.AddSlimeGoalsInfrastructure(Path.Combine(_directory, "slimegoals.db"));
        services.AddSingleton<IReminderFormatter, PlainFormatter>();
        services.AddSingleton<INotificationService>(_notifications);
        _services = services.BuildServiceProvider();
        using var connection = _services.GetRequiredService<SqliteConnectionFactory>().Open();
        MigrationRunner.Migrate(connection);
    }

    public void Dispose()
    {
        _services.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class PlainFormatter : IReminderFormatter
    {
        public ReminderText Format(Goal goal, int periodTotal) => new(goal.Title, "corpo", "+1");
    }

    private sealed class FakeNotifications : INotificationService
    {
        public List<Guid> Visible { get; } = [];
        public event EventHandler<ReminderActionEventArgs>? ActionReceived { add { } remove { } }

        public Task ShowAsync(Reminder reminder, CancellationToken ct = default)
        {
            Visible.Add(reminder.LogId);
            return Task.CompletedTask;
        }

        public Task WithdrawAsync(Guid logId, CancellationToken ct = default)
        {
            Visible.Remove(logId);
            return Task.CompletedTask;
        }
    }

    private async Task<Goal> FiredGoal()
    {
        var goal = new Goal
        {
            Id = Guid.CreateVersion7(), Title = "Meta", Recurrence = Recurrence.Daily, Target = 8, Unit = "copos", Icon = "water",
            Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
            ActiveDays = WeekDays.All, CreatedAt = T0.AddDays(-1), UpdatedAt = T0.AddDays(-1),
        };
        await _services.GetRequiredService<IGoalRepository>().AddAsync(goal);
        await _services.GetRequiredService<ReminderService>().SyncAsync();
        return goal;
    }

    [Fact]
    public async Task Pending_notification_leaves_the_center_when_its_goal_is_deleted()
    {
        var goal = await FiredGoal();
        _notifications.Visible.Should().ContainSingle();

        await _services.GetRequiredService<GoalService>().DeleteAsync(goal.Id);

        _notifications.Visible.Should().BeEmpty();
        (await _services.GetRequiredService<IReminderLogRepository>().ListPendingAsync()).Should().BeEmpty();
    }

    // A notificação pode sobrar: o processo morreu depois do COMMIT, ou o sistema não a retirou.
    [Theory]
    [InlineData(ReminderAction.Primary)]
    [InlineData(ReminderAction.Snooze)]
    [InlineData(ReminderAction.Ignore)]
    public async Task Tap_on_a_notification_left_over_from_a_deleted_goal_is_stale(ReminderAction action)
    {
        var goal = await FiredGoal();
        var logId = _notifications.Visible.Single();
        await _services.GetRequiredService<GoalService>().DeleteAsync(goal.Id);

        var outcome = await _services.GetRequiredService<ReminderResponseHandler>().HandleAsync(logId, action);

        outcome.Should().Be(ReminderOutcome.Stale);
    }
}
