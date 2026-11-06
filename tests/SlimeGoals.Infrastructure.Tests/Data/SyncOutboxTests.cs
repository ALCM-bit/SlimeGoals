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

    private void MarkKnown(Guid id, string kind, int seq) => _db.Anchor.Execute(
        "INSERT INTO sync_records (record_id, kind, seq) VALUES (@Id, @Kind, @Seq)", new { Id = id.ToString("D"), Kind = kind, Seq = seq });

    private static string Text(Guid id) => id.ToString("D");

    [Fact]
    public async Task Deleting_with_sync_off_touches_no_sync_table()
    {
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        await Checkins.AddAsync(NewCheckin(goal));

        await Goals.DeleteAsync(goal.Id);

        Count("sync_outbox").Should().Be(0);
        Count("sync_records").Should().Be(0);
        Count("sync_tombstones").Should().Be(0);
        Count("sync_pending").Should().Be(0);
    }

    [Fact]
    public async Task Deleting_goal_the_server_knows_queues_delete_and_tombstone()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        MarkKnown(goal.Id, "goal", 5);

        await Goals.DeleteAsync(goal.Id);

        var row = await _db.Anchor.QuerySingleAsync("SELECT kind, op, base_seq FROM sync_outbox WHERE record_id = @Id", new { Id = Text(goal.Id) });
        ((string)row.kind).Should().Be("goal");
        ((string)row.op).Should().Be("delete");
        ((long)row.base_seq).Should().Be(5);
        (await _db.Anchor.QueryAsync<string>("SELECT goal_id FROM sync_tombstones")).Should().Equal(Text(goal.Id));
    }

    [Fact]
    public async Task Deleting_goal_never_sent_only_leaves_the_queue()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);

        await Goals.DeleteAsync(goal.Id);

        Count("sync_outbox").Should().Be(0);
        (await _db.Anchor.QueryAsync<string>("SELECT goal_id FROM sync_tombstones")).Should().Equal(Text(goal.Id));
    }

    [Fact]
    public async Task Deleting_goal_handles_each_checkin()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        var known = NewCheckin(goal);
        var unsent = NewCheckin(goal);
        await Checkins.AddAsync(known);
        await Checkins.AddAsync(unsent);
        MarkKnown(known.Id, "checkin", 3);

        await Goals.DeleteAsync(goal.Id);

        var rows = (await _db.Anchor.QueryAsync("SELECT record_id, kind, op, base_seq FROM sync_outbox")).ToList();
        rows.Should().ContainSingle();
        ((string)rows[0].record_id).Should().Be(Text(known.Id));
        ((string)rows[0].kind).Should().Be("checkin");
        ((string)rows[0].op).Should().Be("delete");
        ((long)rows[0].base_seq).Should().Be(3);
    }

    [Fact]
    public async Task Deleting_goal_purges_pending_checkins()
    {
        EnableSync();
        var goal = NewGoal();
        var other = NewGoal();
        await Goals.AddAsync(goal);
        await Goals.AddAsync(other);
        foreach (var g in new[] { goal, other })
            _db.Anchor.Execute("INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES (@R, @G, 1, '{}')",
                new { R = Text(Guid.CreateVersion7()), G = Text(g.Id) });

        await Goals.DeleteAsync(goal.Id);

        (await _db.Anchor.QueryAsync<string>("SELECT goal_id FROM sync_pending")).Should().Equal(Text(other.Id));
    }

    [Fact]
    public async Task Deleting_goal_leaves_other_goals_queue_intact()
    {
        EnableSync();
        var goal = NewGoal();
        var other = NewGoal();
        await Goals.AddAsync(goal);
        await Goals.AddAsync(other);
        var otherCheckin = NewCheckin(other);
        await Checkins.AddAsync(otherCheckin);
        await Checkins.AddAsync(NewCheckin(goal));

        await Goals.DeleteAsync(goal.Id);

        (await _db.Anchor.QueryAsync<string>("SELECT record_id FROM sync_outbox"))
            .Should().BeEquivalentTo(Text(other.Id), Text(otherCheckin.Id));
    }

    [Fact]
    public async Task Deleting_missing_goal_changes_no_sync_table()
    {
        EnableSync();
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        var ghost = Guid.CreateVersion7();
        _db.Anchor.Execute("INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES (@R, @G, 1, '{}')",
            new { R = Text(Guid.CreateVersion7()), G = Text(ghost) });

        var delete = () => Goals.DeleteAsync(ghost);

        await delete.Should().ThrowAsync<GoalNotFoundException>();
        Count("sync_outbox").Should().Be(1);
        Count("sync_tombstones").Should().Be(0);
        Count("sync_pending").Should().Be(1);
    }
}
