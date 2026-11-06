using AwesomeAssertions;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class SyncOutboxTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private GoalRepository Goals => new(_db.Factory);
    private CheckinRepository Checkins => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    private static Checkin NewCheckin(Goal goal) => new()
    {
        Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = T0, Amount = 1, UpdatedAt = T0,
    };

    private void EnableSync() => _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");

    private int Count(string table) => _db.Anchor.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");

    private void FailOutboxInserts() => _db.Anchor.Execute(
        "CREATE TRIGGER fail_outbox BEFORE INSERT ON sync_outbox BEGIN SELECT RAISE(ABORT, 'teste'); END;");

    [Fact]
    public async Task Disabled_sync_writes_nothing_to_queue()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Goals.UpdateAsync(goal with { Title = "Outra" });
        await Checkins.AddAsync(NewCheckin(goal));

        Count("sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Goal_add_and_update_keep_one_line()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Goals.UpdateAsync(goal with { Title = "Outra" });

        var rows = (await _db.Anchor.QueryAsync("SELECT kind, op, revision FROM sync_outbox")).ToList();
        rows.Should().ContainSingle();
        ((string)rows[0].kind).Should().Be("goal");
        ((string)rows[0].op).Should().Be("upsert");
        ((long)rows[0].revision).Should().Be(2);
    }

    [Fact]
    public async Task Checkin_add_is_queued()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Checkins.AddAsync(NewCheckin(goal));

        _db.Anchor.ExecuteScalar<int>("SELECT COUNT(*) FROM sync_outbox WHERE kind = 'checkin'").Should().Be(1);
    }

    [Fact]
    public async Task Replaced_item_keeps_its_position()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Checkins.AddAsync(NewCheckin(goal));
        await Goals.UpdateAsync(goal with { Title = "Outra" });

        var kinds = (await _db.Anchor.QueryAsync<string>("SELECT kind FROM sync_outbox ORDER BY position")).ToList();
        kinds.Should().Equal("goal", "checkin");
    }

    [Fact]
    public async Task Base_seq_comes_from_sync_records()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        _db.Anchor.ExecuteScalar<int>("SELECT base_seq FROM sync_outbox").Should().Be(0);

        _db.Anchor.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES (@Id, 'goal', 7)", new { Id = goal.Id.ToString("D") });
        await Goals.UpdateAsync(goal with { Title = "Outra" });

        _db.Anchor.ExecuteScalar<int>("SELECT base_seq FROM sync_outbox").Should().Be(7);
    }

    [Fact]
    public async Task Queue_failure_rolls_back_the_data()
    {
        EnableSync();
        var goal = NewGoal();
        FailOutboxInserts();

        var addGoal = () => Goals.AddAsync(goal);
        await addGoal.Should().ThrowAsync<SqliteException>();
        Count("goals").Should().Be(0);

        _db.Anchor.Execute("DROP TRIGGER fail_outbox");
        await Goals.AddAsync(goal);
        _db.Anchor.Execute("DELETE FROM sync_outbox");
        FailOutboxInserts();

        var addCheckin = () => Checkins.AddAsync(NewCheckin(goal));
        await addCheckin.Should().ThrowAsync<SqliteException>();
        Count("checkins").Should().Be(0);
    }

    [Fact]
    public async Task Update_of_missing_goal_queues_nothing()
    {
        EnableSync();

        var update = () => Goals.UpdateAsync(NewGoal());

        await update.Should().ThrowAsync<GoalNotFoundException>();
        Count("sync_outbox").Should().Be(0);
    }

    [Fact]
    public async Task Checkin_of_missing_goal_queues_nothing()
    {
        EnableSync();

        var add = () => Checkins.AddAsync(NewCheckin(NewGoal()));

        await add.Should().ThrowAsync<GoalNotFoundException>();
        Count("sync_outbox").Should().Be(0);
    }
}
