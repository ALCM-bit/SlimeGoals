using Dapper;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ISyncStateStore"/>: tabela sync_state e as tabelas da fila.</summary>
public sealed class SyncStateRepository(SqliteConnectionFactory db) : ISyncStateStore
{
    private sealed record StateRow(long Enabled, string? DeviceId, long LastSeq);

    public async Task<SyncState> GetAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        var row = await c.QuerySingleAsync<StateRow>(new CommandDefinition(
            "SELECT enabled AS Enabled, device_id AS DeviceId, last_seq AS LastSeq FROM sync_state",
            cancellationToken: ct));
        return new SyncState(row.Enabled == 1, row.DeviceId is null ? null : Guid.Parse(row.DeviceId), row.LastSeq);
    }

    public async Task EnableAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        if (await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT enabled FROM sync_state", transaction: tx, cancellationToken: ct)) == 1)
            return;

        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE sync_state SET enabled = 1, device_id = COALESCE(device_id, @New)",
            new { New = SqlText.Id(Guid.NewGuid()) }, tx, cancellationToken: ct));
        // SQL direto, sem passar pelo modelo: uma linha inválida não pode impedir de ligar. As tags padrão intocadas
        // (data de 1970) ficam de fora: qualquer mudança feita em outro aparelho vence (Tags_v1 §4.4.7).
        await c.ExecuteAsync(new CommandDefinition($"""
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT id, '{SyncOutbox.Goal}', 'upsert', 0 FROM goals ORDER BY created_at;
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT id, '{SyncOutbox.Checkin}', 'upsert', 0 FROM checkins ORDER BY done_at;
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT id, '{SyncOutbox.Tag}', 'upsert', 0 FROM tags WHERE updated_at <> @Seed ORDER BY name;
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT id, '{SyncOutbox.GoalTag}', 'upsert', 0 FROM goal_tags ORDER BY goal_id, tag_name;
            """, new { Seed = SqlText.Instant(DefaultTags.Seed) }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task DisableAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            DELETE FROM sync_outbox;
            DELETE FROM sync_records;
            DELETE FROM sync_tombstones;
            DELETE FROM sync_pending;
            UPDATE sync_state SET enabled = 0, last_seq = 0, last_sync_at = NULL, last_error = NULL, full_resync = 0;
            """, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
