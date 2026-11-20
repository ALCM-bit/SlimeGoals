using System.Globalization;
using Dapper;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 009: metas de tempo passam de minutos para segundos.</summary>
public class Migration009Tests
{
    private const string Now = "2026-11-15T12:00:00.0000000+00:00";
    private const string Old = "2026-11-10T08:00:00.0000000+00:00";

    private static void InsertGoal(Microsoft.Data.Sqlite.SqliteConnection c, Guid id, string measure, int target)
    {
        var unit = measure == "quantity" ? "vezes" : null;
        c.Execute("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                               active_days, created_at, updated_at, measure, unit)
            VALUES (@Id, 'Meta', 'daily', @Target, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Old, @Old, @Measure, @Unit)
            """, new { Id = id.ToString("D"), Target = target, Measure = measure, Unit = unit, Old });
    }

    private static Guid InsertCheckin(Microsoft.Data.Sqlite.SqliteConnection c, Guid goal, int amount,
        string? deletedAt = null, string? compensationPeriod = null)
    {
        var id = Guid.CreateVersion7();
        c.Execute("""
            INSERT INTO checkins (id, goal_id, done_at, amount, updated_at, deleted_at, compensation_period)
            VALUES (@Id, @Goal, @Old, @Amount, @Old, @Deleted, @Comp)
            """, new { Id = id.ToString("D"), Goal = goal.ToString("D"), Amount = amount, Deleted = deletedAt, Comp = compensationPeriod, Old });
        return id;
    }

    [Fact]
    public void Duration_goals_and_their_checkins_become_seconds()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 8);
        var goal = Guid.CreateVersion7();
        InsertGoal(c, goal, "duration", 90);
        var ids = new[]
        {
            InsertCheckin(c, goal, 25),
            InsertCheckin(c, goal, -5),
            InsertCheckin(c, goal, 15, deletedAt: Old),
            InsertCheckin(c, goal, 10, compensationPeriod: "2026-11-09"),
        };

        MigrationRunner.Migrate(c).Should().Be(9);

        c.ExecuteScalar<int>("SELECT target FROM goals WHERE id = @Id", new { Id = goal.ToString("D") }).Should().Be(5400);
        ids.Select(i => c.ExecuteScalar<int>("SELECT amount FROM checkins WHERE id = @Id", new { Id = i.ToString("D") }))
            .Should().Equal(1500, -300, 900, 600);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM goals WHERE updated_at <> @Old", new { Old }).Should().Be(0);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM checkins WHERE updated_at <> @Old", new { Old }).Should().Be(0);
    }

    [Fact]
    public void Quantity_and_completion_are_untouched()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 8);
        var quantity = Guid.CreateVersion7();
        var completion = Guid.CreateVersion7();
        InsertGoal(c, quantity, "quantity", 8);
        InsertGoal(c, completion, "completion", 1);
        var q = InsertCheckin(c, quantity, 3);
        var d = InsertCheckin(c, completion, 1);
        var duration = Guid.CreateVersion7();
        InsertGoal(c, duration, "duration", 30);
        var t = InsertCheckin(c, duration, 7);

        MigrationRunner.Migrate(c);

        c.ExecuteScalar<int>("SELECT target FROM goals WHERE id = @Id", new { Id = duration.ToString("D") }).Should().Be(1800);
        c.ExecuteScalar<int>("SELECT amount FROM checkins WHERE id = @Id", new { Id = t.ToString("D") }).Should().Be(420);

        c.ExecuteScalar<int>("SELECT target FROM goals WHERE id = @Id", new { Id = quantity.ToString("D") }).Should().Be(8);
        c.ExecuteScalar<int>("SELECT target FROM goals WHERE id = @Id", new { Id = completion.ToString("D") }).Should().Be(1);
        c.ExecuteScalar<int>("SELECT amount FROM checkins WHERE id = @Id", new { Id = q.ToString("D") }).Should().Be(3);
        c.ExecuteScalar<int>("SELECT amount FROM checkins WHERE id = @Id", new { Id = d.ToString("D") }).Should().Be(1);
    }

    [Fact]
    public async Task Held_focus_chunks_become_seconds()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 8);
        var chunk = Guid.CreateVersion7();
        c.Execute("""
            INSERT INTO focus_session (id, plan, goal_id, started_at, step, block_started_at, cut_at, carry_ms, revision)
            VALUES (1, '{"blocks":[{"kind":"focus","minutes":25}],"repeat":1}', NULL, @Now, 0, @Now, @Now, 42000, 5)
            """, new { Now });
        c.Execute("INSERT INTO focus_chunk (id, minutes, task, ended_at) VALUES (@Id, 25, NULL, @Now)",
            new { Id = chunk.ToString("D"), Now });

        MigrationRunner.Migrate(c);

        var repo = new FocusSessionRepository(db.Factory);
        var session = await repo.GetAsync();
        session.Should().NotBeNull();
        session!.Carry.Should().Be(TimeSpan.FromSeconds(42));
        session.Plan.Blocks.Should().ContainSingle().Which.Minutes.Should().Be(25);
        session.Plan.Repeat.Should().Be(1);
        session.Step.Should().Be(0);
        session.Revision.Should().Be(5);
        session.StartedAt.Should().Be(DateTimeOffset.Parse(Now, CultureInfo.InvariantCulture));
        (await repo.ChunksAsync()).Should().ContainSingle().Which.Seconds.Should().Be(1500);
    }

    [Fact]
    public void Pending_sync_rows_are_format_1()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 8);
        c.Execute("INSERT INTO sync_pending (record_id, goal_id, seq, payload) VALUES ('r1', 'g1', 3, '{}')");

        MigrationRunner.Migrate(c);

        c.ExecuteScalar<int>("SELECT format_version FROM sync_pending WHERE record_id = 'r1'").Should().Be(1);
    }

    [Fact]
    public void Nothing_enters_the_outbox()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 8);
        var goal = Guid.CreateVersion7();
        InsertGoal(c, goal, "duration", 60);
        InsertCheckin(c, goal, 20);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);

        MigrationRunner.Migrate(c);

        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);
    }
}
