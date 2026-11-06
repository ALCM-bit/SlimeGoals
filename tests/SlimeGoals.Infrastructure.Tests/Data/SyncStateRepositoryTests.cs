using AwesomeAssertions;
using Dapper;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class SyncStateRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private SyncStateRepository Sync => new(_db.Factory);
    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Goal NewGoal(DateTimeOffset createdAt) => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = createdAt, UpdatedAt = createdAt, Unit = "copos",
    };

    private static Checkin NewCheckin(Goal goal, DateTimeOffset doneAt) => new()
    {
        Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = doneAt, Amount = 1, UpdatedAt = doneAt,
    };

    private int Count(string table) => _db.Anchor.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");

    private async Task<(Goal, Goal)> SeedAsync()
    {
        var g1 = NewGoal(T0);
        var g2 = NewGoal(T0.AddHours(1));
        await Goals.AddAsync(g2);
        await Goals.AddAsync(g1);
        await Checkins.AddAsync(NewCheckin(g1, T0.AddDays(2)));
        await Checkins.AddAsync(NewCheckin(g1, T0.AddDays(1)));
        await Checkins.AddAsync(NewCheckin(g2, T0.AddDays(3)));
        return (g1, g2);
    }

    [Fact]
    public async Task Starts_disabled() =>
        (await Sync.GetAsync()).Should().Be(new SyncState(false, null, 0));

    [Fact]
    public async Task Enable_queues_goals_before_checkins()
    {
        await SeedAsync();
        await Sync.EnableAsync();

        Count("sync_outbox").Should().Be(5);
        var kinds = _db.Anchor.Query<string>("SELECT kind FROM sync_outbox ORDER BY position").ToList();
        kinds.Should().Equal("goal", "goal", "checkin", "checkin", "checkin");
        _db.Anchor.Query<string>("SELECT op FROM sync_outbox").Should().OnlyContain(o => o == "upsert");
        (await Sync.GetAsync()).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_queues_goals_by_creation_and_checkins_by_time()
    {
        var (g1, g2) = await SeedAsync();
        await Sync.EnableAsync();

        var ids = _db.Anchor.Query<string>("SELECT record_id FROM sync_outbox ORDER BY position").ToList();
        ids.Take(2).Should().Equal(g1.Id.ToString("D"), g2.Id.ToString("D"));
        var doneAts = _db.Anchor.Query<string>(
            "SELECT c.done_at FROM sync_outbox o JOIN checkins c ON c.id = o.record_id WHERE o.kind = 'checkin' ORDER BY o.position").ToList();
        doneAts.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Enable_twice_changes_nothing()
    {
        await SeedAsync();
        await Sync.EnableAsync();
        var first = await Sync.GetAsync();
        await Sync.EnableAsync();

        (await Sync.GetAsync()).DeviceId.Should().Be(first.DeviceId).And.NotBeNull();
        Count("sync_outbox").Should().Be(5);
        _db.Anchor.Query<int>("SELECT revision FROM sync_outbox").Should().OnlyContain(r => r == 1);
    }

    [Fact]
    public async Task Enable_keeps_invalid_rows_from_stopping_it()
    {
        await Goals.AddAsync(NewGoal(T0));
        _db.Anchor.Execute("UPDATE goals SET color = 'invalida'");

        var act = () => Sync.EnableAsync();

        await act.Should().NotThrowAsync();
        Count("sync_outbox").Should().Be(1);
    }

    [Fact]
    public async Task Disable_clears_sync_tables_and_keeps_data()
    {
        var (g1, _) = await SeedAsync();
        await Sync.EnableAsync();
        _db.Anchor.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES ('r1', 'goal', 3)");
        _db.Anchor.Execute("INSERT INTO sync_tombstones (goal_id) VALUES ('g9')");
        _db.Anchor.Execute("INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES ('r2', 'g9', 4, '{}')");
        _db.Anchor.Execute("UPDATE sync_state SET last_seq = 9, last_sync_at = '2026-11-02T12:00:00.0000000+00:00', last_error = 'x'");

        await Sync.DisableAsync();

        foreach (var table in new[] { "sync_outbox", "sync_records", "sync_tombstones", "sync_pending" })
            Count(table).Should().Be(0, table);
        (await Sync.GetAsync()).Should().Match<SyncState>(s => !s.Enabled && s.LastSeq == 0);
        _db.Anchor.ExecuteScalar<string?>("SELECT last_sync_at FROM sync_state").Should().BeNull();
        _db.Anchor.ExecuteScalar<string?>("SELECT last_error FROM sync_state").Should().BeNull();
        Count("goals").Should().Be(2);
        Count("checkins").Should().Be(3);
        (await Goals.GetAsync(g1.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Enable_after_disable_keeps_device_id()
    {
        await Sync.EnableAsync();
        var device = (await Sync.GetAsync()).DeviceId;
        await Sync.DisableAsync();
        await Sync.EnableAsync();

        (await Sync.GetAsync()).DeviceId.Should().Be(device).And.NotBeNull();
    }

    [Fact]
    public async Task Writes_after_disable_are_not_queued()
    {
        await Sync.EnableAsync();
        await Sync.DisableAsync();
        var goal = NewGoal(T0);
        await Goals.AddAsync(goal);
        await Checkins.AddAsync(NewCheckin(goal, T0));

        Count("sync_outbox").Should().Be(0);
    }
}
