using Dapper;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 006: colunas do motor de sincronização.</summary>
public class Migration006Tests
{
    private const string Now = "2026-11-06T12:00:00.0000000+00:00";

    [Fact]
    public void Upgrade_from_5_keeps_checkins_as_not_superseded()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 5);
        c.Execute("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                               active_days, created_at, updated_at, measure, unit)
            VALUES ('g1', 'Ler', 'daily', 3, 'book', 'pink', 45, '09:00', '18:00', '1,2,3', @Now, @Now, 'quantity', 'vezes')
            """, new { Now });
        c.Execute("INSERT INTO checkins (id, goal_id, done_at, note, updated_at) VALUES ('c1', 'g1', @Now, 'nota', @Now)", new { Now });

        MigrationRunner.Migrate(c).Should().Be(6);

        c.ExecuteScalar<long>("PRAGMA user_version").Should().Be(6);
        var row = c.QuerySingle("SELECT superseded, compensation_period AS Period, compensation_seen AS Seen FROM checkins WHERE id = 'c1'");
        ((long)row.superseded).Should().Be(0);
        ((object?)row.Period).Should().BeNull();
        ((object?)row.Seen).Should().BeNull();
        c.ExecuteScalar<long>("SELECT full_resync FROM sync_state").Should().Be(0);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('sync_outbox') WHERE name = 'record_key'").Should().Be(1);
    }

    /// <summary>Cada exclusão recebida procura a chave também na fila: sem índice, varreria a fila inteira, que ao ligar tem tudo.</summary>
    [Fact]
    public void Outbox_is_searched_by_key_through_an_index()
    {
        using var db = new TestDatabase();

        var plan = db.Anchor.Query("EXPLAIN QUERY PLAN SELECT record_id, kind FROM sync_outbox WHERE record_key = 'k'")
            .Select(r => (string)r.detail).ToList();

        plan.Should().ContainSingle().Which.Should().Contain("USING INDEX");
    }
}
