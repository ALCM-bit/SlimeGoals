using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class MigrationRunnerTests
{
    private const int Latest = 2;

    private const string GoalColumns =
        "id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until, active_days, created_at, updated_at";

    private static string GoalValues(string id, string recurrence, int target) =>
        $"'{id}', 't', '{recurrence}', {target}, 'book', 'purple', 60, '09:00', '18:00', '1,2,3', '2026-11-02T12:00:00.0000000+00:00', '2026-11-02T12:00:00.0000000+00:00'";

    [Fact]
    public void Migrate_on_empty_db_creates_tables_and_sets_latest_version()
    {
        using var db = new TestDatabase(migrate: false);

        MigrationRunner.Migrate(db.Anchor).Should().Be(Latest);

        db.Anchor.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'")
            .Should().Contain(["goals", "checkins", "reminder_log", "settings"]);
        db.Anchor.ExecuteScalar<long>("PRAGMA user_version").Should().Be(Latest);
    }

    [Fact]
    public void Migrate_twice_is_a_no_op()
    {
        using var db = new TestDatabase(migrate: false);
        MigrationRunner.Migrate(db.Anchor);

        MigrationRunner.Migrate(db.Anchor).Should().Be(Latest);
    }

    [Fact]
    public void UpTo_below_current_version_is_a_no_op()
    {
        using var db = new TestDatabase();

        MigrationRunner.Migrate(db.Anchor, upTo: 1).Should().Be(Latest);
    }

    [Fact]
    public void Once_recurrence_violates_check()
    {
        using var db = new TestDatabase();
        var act = () => db.Anchor.Execute($"INSERT INTO goals ({GoalColumns}) VALUES ({GoalValues("a", "once", 1)})");
        act.Should().Throw<SqliteException>().Where(e => e.Message.Contains("CHECK"));
    }

    [Fact]
    public void Unknown_recurrence_violates_check()
    {
        using var db = new TestDatabase();
        var act = () => db.Anchor.Execute($"INSERT INTO goals ({GoalColumns}) VALUES ({GoalValues("a", "monthly", 1)})");
        act.Should().Throw<SqliteException>().Where(e => e.Message.Contains("CHECK"));
    }

    [Fact]
    public void Checkin_with_unknown_goal_violates_foreign_key()
    {
        using var db = new TestDatabase();
        var act = () => db.Anchor.Execute(
            "INSERT INTO checkins (id, goal_id, done_at, updated_at) VALUES ('c', 'nope', '2026-11-02T12:00:00Z', '2026-11-02T12:00:00Z')");
        act.Should().Throw<SqliteException>().Where(e => e.Message.Contains("FOREIGN KEY"));
    }
}

public class Migration002Tests
{
    private const string Columns =
        "id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until, active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at";

    private static void InsertGoal(SqliteConnection c, string id, string recurrence, int target, string? archived = null, string? deleted = null) =>
        c.Execute($"INSERT INTO goals ({Columns}) VALUES (@id, 'Meta ' || @id, @recurrence, @target, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', '2026-11-01', '2026-12-20', '2026-11-02T12:00:00Z', '2026-11-02T13:00:00Z', @archived, @deleted)",
            new { id, recurrence, target, archived, deleted });

    private static void InsertCheckin(SqliteConnection c, string id, string goalId) =>
        c.Execute("INSERT INTO checkins (id, goal_id, done_at, updated_at) VALUES (@id, @goalId, '2026-11-02T12:00:00Z', '2026-11-02T12:00:00Z')", new { id, goalId });

    private static void InsertReminder(SqliteConnection c, string id, string goalId) =>
        c.Execute("INSERT INTO reminder_log (id, goal_id, fired_at) VALUES (@id, @goalId, '2026-11-02T12:00:00Z')", new { id, goalId });

    /// <summary>Banco na versão 1 com metas dos três tipos, check-ins e lembretes.</summary>
    private static TestDatabase V1WithData()
    {
        var db = new TestDatabase(migrate: false);
        MigrationRunner.Migrate(db.Anchor, upTo: 1).Should().Be(1);
        var c = db.Anchor;
        InsertGoal(c, "d", "daily", 3);
        InsertGoal(c, "w", "weekly", 5, archived: "2026-11-03T00:00:00Z");
        InsertGoal(c, "o1", "once", 1);
        InsertGoal(c, "o2", "once", 1, deleted: "2026-11-03T00:00:00Z");
        c.Execute("UPDATE goals SET completed_at = '2026-11-02T15:00:00Z', archived_at = '2026-11-02T15:00:00Z' WHERE id = 'o1'");
        InsertCheckin(c, "cd", "d");
        InsertCheckin(c, "co", "o1");
        InsertReminder(c, "rw", "w");
        InsertReminder(c, "ro", "o1");
        return db;
    }

    [Fact]
    public void Deletes_once_goals_with_their_checkins_and_reminders()
    {
        using var db = V1WithData();

        MigrationRunner.Migrate(db.Anchor).Should().Be(2);

        db.Anchor.Query<string>("SELECT id FROM goals ORDER BY id").Should().Equal("d", "w");
        db.Anchor.Query<string>("SELECT id FROM checkins").Should().Equal("cd");
        db.Anchor.Query<string>("SELECT id FROM reminder_log").Should().Equal("rw");
    }

    [Fact]
    public void Keeps_daily_and_weekly_goals_intact()
    {
        using var db = V1WithData();
        const string select = "SELECT id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until, active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at FROM goals WHERE id IN ('d','w') ORDER BY id";
        var before = db.Anchor.Query(select).Select(r => (IDictionary<string, object>)r).ToList();

        MigrationRunner.Migrate(db.Anchor);

        db.Anchor.Query(select).Select(r => (IDictionary<string, object>)r).Should().BeEquivalentTo(before);
    }

    [Fact]
    public void Drops_completed_at_and_keeps_foreign_keys_valid()
    {
        using var db = V1WithData();

        MigrationRunner.Migrate(db.Anchor);

        db.Anchor.Query<string>("SELECT name FROM pragma_table_info('goals')").Should().NotContain("completed_at");
        db.Anchor.Query("PRAGMA foreign_key_check").Should().BeEmpty();
        var orphan = () => InsertCheckin(db.Anchor, "x", "nope");
        orphan.Should().Throw<SqliteException>().Where(e => e.Message.Contains("FOREIGN KEY"));
    }
}

public class MigrationRunnerConcurrencyTests
{
    // Revisão final #5a: dois processos abrindo um banco novo ao mesmo tempo.
    [Fact]
    public async Task Concurrent_first_run_migrates_once_without_errors()
    {
        for (var round = 0; round < 10; round++)
        {
            var path = Path.Combine(Path.GetTempPath(), $"slimegoals-race-{Guid.NewGuid():N}.db");
            try
            {
                var factory = new SqliteConnectionFactory($"Data Source={path}");
                using var gate = new Barrier(4);
                var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                {
                    using var c = factory.Open();
                    gate.SignalAndWait();
                    return MigrationRunner.Migrate(c);
                }));

                (await Task.WhenAll(runs)).Should().AllBeEquivalentTo(2);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(path);
            }
        }
    }

    // Revisão final #5b: banco criado por uma versão mais nova do app.
    [Fact]
    public void Database_newer_than_app_is_rejected()
    {
        using var db = new TestDatabase(migrate: false);
        db.Anchor.Execute("PRAGMA user_version = 99");

        var act = () => MigrationRunner.Migrate(db.Anchor);

        act.Should().Throw<InvalidOperationException>().WithMessage("*99*");
    }
}
