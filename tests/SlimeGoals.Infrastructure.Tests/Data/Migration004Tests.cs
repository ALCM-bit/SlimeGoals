using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 004: apaga de vez o que a exclusão lógica deixou no banco.</summary>
public class Migration004Tests
{
    private const string Now = "2026-11-02T12:00:00.0000000+00:00";

    private static void InsertGoal(SqliteConnection c, string id, string title = "t", string? deleted = null) =>
        c.Execute("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                               active_days, created_at, updated_at, deleted_at, measure, unit)
            VALUES (@id, @title, 'daily', 3, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Now, @Now, @deleted, 'quantity', 'vezes')
            """, new { id, title, deleted, Now });

    private static void InsertCheckin(SqliteConnection c, string id, string goalId, string? deleted = null, string? note = null) =>
        c.Execute("INSERT INTO checkins (id, goal_id, done_at, note, updated_at, deleted_at) VALUES (@id, @goalId, @Now, @note, @Now, @deleted)",
            new { id, goalId, note, deleted, Now });

    private static void InsertLog(SqliteConnection c, string id, string goalId) =>
        c.Execute("INSERT INTO reminder_log (id, goal_id, fired_at) VALUES (@id, @goalId, @Now)", new { id, goalId, Now });

    [Fact]
    public void Removes_deleted_goals_with_everything_linked_and_keeps_the_rest()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 3);
        InsertGoal(c, "gone", deleted: Now);
        InsertGoal(c, "kept");
        InsertCheckin(c, "c-gone", "gone");
        InsertCheckin(c, "c-kept", "kept");
        InsertLog(c, "l-gone", "gone");
        InsertLog(c, "l-kept", "kept");
        c.Execute("INSERT INTO active_timer (id, goal_id, started_at) VALUES (1, 'gone', @Now)", new { Now });

        MigrationRunner.Migrate(c).Should().Be(6);

        c.Query<string>("SELECT id FROM goals").Should().Equal("kept");
        c.Query<string>("SELECT id FROM checkins").Should().Equal("c-kept");
        c.Query<string>("SELECT id FROM reminder_log").Should().Equal("l-kept");
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM active_timer").Should().Be(0);
    }

    [Fact]
    public void Keeps_the_timer_of_a_live_goal()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 3);
        InsertGoal(c, "kept");
        c.Execute("INSERT INTO active_timer (id, goal_id, started_at) VALUES (1, 'kept', @Now)", new { Now });

        MigrationRunner.Migrate(c);

        c.ExecuteScalar<long>("SELECT COUNT(*) FROM active_timer").Should().Be(1);
    }

    [Fact]
    public void Removes_deleted_checkins_of_live_goals()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 3);
        InsertGoal(c, "kept");
        InsertCheckin(c, "gone", "kept", deleted: Now);
        InsertCheckin(c, "stays", "kept");

        MigrationRunner.Migrate(c);

        c.Query<string>("SELECT id FROM checkins").Should().Equal("stays");
    }

    [Fact]
    public void Keeps_the_deleted_at_columns()
    {
        using var db = new TestDatabase();

        foreach (var table in new[] { "goals", "checkins" })
            db.Anchor.Query<string>($"SELECT name FROM pragma_table_info('{table}')").Should().Contain("deleted_at", table);
    }

    // A exclusão lógica era um UPDATE sem secure_delete: sobrava a cópia velha da linha na página, e as recriações de
    // goals (002, 003) deixavam páginas livres com os títulos. Só o DELETE da 004 não limpa nada disso.
    [Fact]
    public void Content_deleted_before_the_upgrade_is_not_left_readable_in_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"slimegoals-m004-{Guid.NewGuid():N}.db");
        try
        {
            using (var old = new SqliteConnection($"Data Source={path}"))
            {
                old.Open();
                old.Execute("PRAGMA secure_delete = OFF");
                MigrationRunner.Migrate(old, upTo: 2);
                old.Execute("""
                    INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                                       active_days, created_at, updated_at)
                    VALUES ('recreated', 'TituloAntigoXyz', 'daily', 3, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Now, @Now)
                    """, new { Now });
                MigrationRunner.Migrate(old, upTo: 3);
                InsertGoal(old, "gone", "TituloSensivelXyz");
                InsertGoal(old, "kept", "TituloQueFica");
                InsertCheckin(old, "c1", "gone", note: "NotaSensivelXyz");
                old.Execute("UPDATE goals SET deleted_at = @Now, updated_at = @Now WHERE id IN ('gone', 'recreated')", new { Now });
            }
            SqliteConnection.ClearAllPools();

            var factory = new SqliteConnectionFactory($"Data Source={path}");
            using (var c = factory.Open())
                MigrationRunner.Migrate(c).Should().Be(6);

            SqliteConnection.ClearAllPools();
            var file = File.ReadAllBytes(path);
            file.AsSpan().IndexOf("TituloQueFica"u8).Should().BeGreaterThan(0);
            file.AsSpan().IndexOf("TituloSensivelXyz"u8).Should().Be(-1);
            file.AsSpan().IndexOf("NotaSensivelXyz"u8).Should().Be(-1);
            file.AsSpan().IndexOf("TituloAntigoXyz"u8).Should().Be(-1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }
}
