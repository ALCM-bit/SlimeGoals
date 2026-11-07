using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 005: tabelas da sincronização, com ela desligada e a fila vazia.</summary>
public class Migration005Tests
{
    private const string Now = "2026-11-06T12:00:00.0000000+00:00";

    [Fact]
    public void Upgrade_from_4_keeps_goals_and_checkins()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 4);
        c.Execute("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                               active_days, created_at, updated_at, measure, unit)
            VALUES ('g1', 'Ler', 'daily', 3, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Now, @Now, 'quantity', 'vezes')
            """, new { Now });
        c.Execute("INSERT INTO checkins (id, goal_id, done_at, note, updated_at) VALUES ('c1', 'g1', @Now, 'nota', @Now)", new { Now });

        MigrationRunner.Migrate(c).Should().Be(6);

        c.Query<string>("SELECT title FROM goals WHERE id = 'g1'").Should().Equal("Ler");
        c.Query<string>("SELECT note FROM checkins WHERE id = 'c1'").Should().Equal("nota");
    }

    [Fact]
    public void Upgrade_leaves_sync_disabled_and_queue_empty()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 4);

        MigrationRunner.Migrate(c);

        var state = c.QuerySingle("SELECT enabled, device_id AS DeviceId, last_seq AS LastSeq FROM sync_state");
        ((long)state.enabled).Should().Be(0);
        ((object?)state.DeviceId).Should().BeNull();
        ((long)state.LastSeq).Should().Be(0);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_state").Should().Be(1);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);
    }

    [Fact]
    public void Sync_state_accepts_only_one_row()
    {
        using var db = new TestDatabase();

        var act = () => db.Anchor.Execute("INSERT INTO sync_state (id) VALUES (2)");

        act.Should().Throw<SqliteException>();
    }

    [Theory]
    [InlineData("timer", "upsert")]
    [InlineData("goal", "merge")]
    public void Outbox_refuses_unknown_kind_and_op(string kind, string op)
    {
        using var db = new TestDatabase();

        var act = () => db.Anchor.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('r1', @kind, @op)", new { kind, op });

        act.Should().Throw<SqliteException>();
    }
}
