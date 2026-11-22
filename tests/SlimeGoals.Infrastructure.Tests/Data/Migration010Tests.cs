using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

/// <summary>Migração 010: tags, ligações e as duas tabelas de sincronização recriadas com os novos tipos.</summary>
public class Migration010Tests
{
    [Fact]
    public void Creates_tag_tables_and_three_defaults_with_1970()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 9);

        MigrationRunner.Migrate(c).Should().Be(10);

        var rows = c.Query("SELECT id, name, color, updated_at, deleted_at FROM tags ORDER BY name").ToList();
        rows.Select(r => (string)r.name).Should().Equal("estudos", "medicamentos", "tarefas");
        rows.Select(r => (string)r.color).Should().Equal("purple", "green", "amber");
        rows.Select(r => (string)r.updated_at).Should().AllBe("1970-01-01T00:00:00.0000000+00:00");
        rows.Select(r => (string?)r.deleted_at).Should().AllSatisfy(d => d.Should().BeNull());
        rows.Select(r => Guid.ParseExact((string)r.id, "D")).Should().Equal(
            DefaultTags.All.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => TagIds.ForTag(t.Name)));
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM goal_tags").Should().Be(0);
    }

    [Fact]
    public void Defaults_are_not_queued_even_with_sync_enabled()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 9);
        c.Execute("UPDATE sync_state SET enabled = 1");

        MigrationRunner.Migrate(c);

        c.ExecuteScalar<long>("SELECT COUNT(*) FROM tags").Should().Be(3);
        c.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);
    }

    [Fact]
    public void Outbox_and_records_survive_and_accept_new_kinds()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c, upTo: 9);
        c.Execute("""
            INSERT INTO sync_outbox (position, record_id, kind, op, base_seq, revision, record_key)
            VALUES (7, 'r-out', 'goal', 'delete', 4, 3, 'key-1')
            """);
        c.Execute("INSERT INTO sync_records (record_id, kind, seq, record_key, confirmed) VALUES ('r-rec', 'checkin', 12, 'key-2', 0)");

        MigrationRunner.Migrate(c);

        var outbox = c.QuerySingle("SELECT position, record_id, kind, op, base_seq, revision, record_key FROM sync_outbox");
        ((long)outbox.position).Should().Be(7);
        ((string)outbox.record_id).Should().Be("r-out");
        ((string)outbox.kind).Should().Be("goal");
        ((string)outbox.op).Should().Be("delete");
        ((long)outbox.base_seq).Should().Be(4);
        ((long)outbox.revision).Should().Be(3);
        ((string)outbox.record_key).Should().Be("key-1");
        var rec = c.QuerySingle("SELECT record_id, kind, seq, record_key, confirmed FROM sync_records");
        ((string)rec.record_id).Should().Be("r-rec");
        ((string)rec.kind).Should().Be("checkin");
        ((long)rec.seq).Should().Be(12);
        ((string)rec.record_key).Should().Be("key-2");
        ((long)rec.confirmed).Should().Be(0);

        c.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('t1', 'goal_tag', 'upsert')");
        c.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('t2', 'tag', 'upsert')");
        c.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES ('t3', 'goal_tag', 1)");
        c.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES ('t4', 'tag', 1)");
        ((long)c.ExecuteScalar<long>("SELECT position FROM sync_outbox WHERE record_id = 't1'")).Should().Be(8);
        ((Action)(() => c.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('x', 'x', 'upsert')")))
            .Should().Throw<SqliteException>();
        ((Action)(() => c.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES ('x', 'x', 1)")))
            .Should().Throw<SqliteException>();
    }

    [Fact]
    public void Outbox_keeps_its_unique_key_defaults_and_indexes()
    {
        using var db = new TestDatabase(migrate: false);
        var c = db.Anchor;
        MigrationRunner.Migrate(c);

        c.Query<string>("SELECT name FROM sqlite_master WHERE type = 'index'").Should().Contain(
            ["idx_sync_outbox_key", "idx_sync_records_key", "idx_goal_tags_goal"]);
        c.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('a', 'goal', 'upsert')");
        var queued = c.QuerySingle("SELECT base_seq, revision, record_key FROM sync_outbox");
        ((long)queued.base_seq).Should().Be(0);
        ((long)queued.revision).Should().Be(1);
        ((string?)queued.record_key).Should().BeNull();
        ((Action)(() => c.Execute("INSERT INTO sync_outbox (record_id, kind, op) VALUES ('a', 'goal', 'upsert')")))
            .Should().Throw<SqliteException>();
        c.Execute("INSERT INTO sync_records (record_id, kind, seq) VALUES ('a', 'goal', 1)");
        var rec = c.QuerySingle("SELECT record_key, confirmed FROM sync_records");
        ((string)rec.record_key).Should().Be("");
        ((long)rec.confirmed).Should().Be(1);
    }
}
