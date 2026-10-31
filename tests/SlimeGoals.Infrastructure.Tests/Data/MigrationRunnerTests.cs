using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class MigrationRunnerTests
{
    private const string GoalColumns =
        "id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until, active_days, created_at, updated_at";

    private static string GoalValues(string id, string recurrence, int target) =>
        $"'{id}', 't', '{recurrence}', {target}, 'book', 'purple', 60, '09:00', '18:00', '1,2,3', '2026-11-02T12:00:00.0000000+00:00', '2026-11-02T12:00:00.0000000+00:00'";

    [Fact]
    public void Migrate_on_empty_db_creates_tables_and_sets_version_1()
    {
        using var db = new TestDatabase(migrate: false);

        MigrationRunner.Migrate(db.Anchor).Should().Be(1);

        db.Anchor.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'")
            .Should().Contain(["goals", "checkins", "reminder_log", "settings"]);
        db.Anchor.ExecuteScalar<long>("PRAGMA user_version").Should().Be(1);
    }

    [Fact]
    public void Migrate_twice_is_a_no_op()
    {
        using var db = new TestDatabase(migrate: false);
        MigrationRunner.Migrate(db.Anchor);

        MigrationRunner.Migrate(db.Anchor).Should().Be(1);
    }

    [Fact]
    public void Once_goal_with_target_2_violates_check()
    {
        using var db = new TestDatabase();
        var act = () => db.Anchor.Execute($"INSERT INTO goals ({GoalColumns}) VALUES ({GoalValues("a", "once", 2)})");
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
