using System.Diagnostics;
using Dapper;
using SlimeGoals.Core.Sync;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ISyncLocalStore"/>: lê a fila, confirma os envios e guarda o estado do ciclo.</summary>
public sealed class SyncLocalStore(SqliteConnectionFactory db) : ISyncLocalStore
{
    private const int BatchSize = 100;

    private sealed record QueueRow(long Position, string RecordId, string Kind, string Op, long BaseSeq, long Revision);

    private sealed record StatusRow(string? LastSyncAt, string? LastError, long Pending);

    public async Task<IReadOnlyList<OutboxItem>> ReadOutboxAsync(int limit, CancellationToken ct = default)
    {
        var items = new List<OutboxItem>();
        var after = 0L;
        await using var c = db.Open();
        while (items.Count < limit)
        {
            var batch = (await c.QueryAsync<QueueRow>(new CommandDefinition("""
                SELECT position AS Position, record_id AS RecordId, kind AS Kind, op AS Op, base_seq AS BaseSeq, revision AS Revision
                FROM sync_outbox WHERE position > @After ORDER BY position LIMIT @Batch
                """, new { After = after, Batch = BatchSize }, cancellationToken: ct))).ToList();
            if (batch.Count == 0)
                break;

            foreach (var row in batch)
            {
                after = row.Position;
                if (items.Count >= limit)
                    break;
                var item = await BuildItemAsync(c, row, ct);
                if (item is not null)
                    items.Add(item);
                else
                    await c.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM sync_outbox WHERE record_id = @Id AND revision = @Revision",
                        new { Id = row.RecordId, row.Revision }, cancellationToken: ct));
            }
        }
        return items;
    }

    /// <summary>Monta o item com o conteúdo atual da linha. Nulo se não dá para enviar (já registrado no Trace).</summary>
    private static async Task<OutboxItem?> BuildItemAsync(Microsoft.Data.Sqlite.SqliteConnection c, QueueRow row, CancellationToken ct)
    {
        var kind = row.Kind == SyncOutbox.Goal ? SyncRecordKind.Goal : SyncRecordKind.Checkin;
        var id = Guid.ParseExact(row.RecordId, "D");
        if (row.Op == "delete")
            return new OutboxItem(id, kind, true, row.BaseSeq, row.Revision, null);

        var parameters = new { Id = row.RecordId };
        string? payload;
        if (kind == SyncRecordKind.Goal)
        {
            var goals = await RowReader.ReadValidAsync<GoalRow, string>(c, new CommandDefinition(
                GoalRepository.SelectColumns + " WHERE id = @Id", parameters, cancellationToken: ct),
                "goals", r => SyncCodec.Encode(r.ToDomain()));
            payload = goals.SingleOrDefault();
        }
        else
        {
            var checkins = await RowReader.ReadValidAsync<CheckinRepository.Row, string>(c, new CommandDefinition("""
                SELECT id AS Id, goal_id AS GoalId, done_at AS DoneAt, amount AS Amount, note AS Note,
                       updated_at AS UpdatedAt, deleted_at AS DeletedAt,
                       compensation_period AS CompensationPeriod, compensation_seen AS CompensationSeen
                FROM checkins WHERE id = @Id
                """, parameters, cancellationToken: ct), "checkins", r => SyncCodec.Encode(r.ToDomain()));
            payload = checkins.SingleOrDefault();
        }

        if (payload is null)
        {
            Trace.TraceWarning($"Item da fila de sincronização sem linha enviável ({row.Kind} {row.RecordId}); saiu da fila.");
            return null;
        }
        return new OutboxItem(id, kind, false, row.BaseSeq, row.Revision, payload);
    }

    public async Task RememberKeysAsync(IReadOnlyList<OutboxKey> keys, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var key in keys)
            await c.ExecuteAsync(new CommandDefinition(
                "UPDATE sync_outbox SET record_key = @RecordKey WHERE record_id = @Id",
                new { Id = SqlText.Id(key.RecordId), key.RecordKey }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task ConfirmPushAsync(IReadOnlyList<PushConfirmation> confirmations, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        foreach (var confirmation in confirmations)
        {
            var p = new
            {
                Id = SqlText.Id(confirmation.RecordId),
                Kind = confirmation.Kind == SyncRecordKind.Goal ? SyncOutbox.Goal : SyncOutbox.Checkin,
                confirmation.RecordKey,
                confirmation.Seq,
                confirmation.Revision,
            };
            if (confirmation.IsDelete)
            {
                var removed = await c.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM sync_outbox WHERE record_id = @Id AND op = 'delete'", p, tx, cancellationToken: ct));
                if (removed > 0)
                    await c.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM sync_records WHERE record_id = @Id", p, tx, cancellationToken: ct));
                continue;
            }

            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO sync_records (record_id, kind, seq, record_key, confirmed)
                VALUES (@Id, @Kind, @Seq, @RecordKey, 1)
                ON CONFLICT (record_id) DO UPDATE SET kind = excluded.kind, seq = excluded.seq,
                    record_key = excluded.record_key, confirmed = 1
                """, p, tx, cancellationToken: ct));
            // Revisão igual: nada mudou desde a leitura. Outra revisão ou uma exclusão na fila: continua, com a base nova.
            await c.ExecuteAsync(new CommandDefinition("""
                DELETE FROM sync_outbox WHERE record_id = @Id AND op = 'upsert' AND revision = @Revision;
                UPDATE sync_outbox SET base_seq = @Seq WHERE record_id = @Id;
                """, p, tx, cancellationToken: ct));
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ApplyPageAsync(IReadOnlyList<IncomingRecord> records, long lastSeq, Guid deviceId, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        var deleted = await SyncApply.ApplyPageAsync(c, tx, records, lastSeq, deviceId, ct);
        await tx.CommitAsync(ct);
        return deleted;
    }

    public async Task BeginFullResyncAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await SyncApply.BeginFullResyncAsync(c, tx, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> FinishFullResyncAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        var removed = await SyncApply.FinishFullResyncAsync(c, tx, ct);
        await tx.CommitAsync(ct);
        return removed;
    }

    public async Task RecordCycleAsync(DateTimeOffset? syncedAt, string? error, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition(
            "UPDATE sync_state SET last_error = @Error, last_sync_at = COALESCE(@At, last_sync_at)",
            new { Error = error, At = syncedAt is { } at ? SqlText.Instant(at) : null }, cancellationToken: ct));
    }

    public async Task<SyncStatus> GetStatusAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        var row = await c.QuerySingleAsync<StatusRow>(new CommandDefinition("""
            SELECT last_sync_at AS LastSyncAt, last_error AS LastError, (SELECT COUNT(*) FROM sync_outbox) AS Pending
            FROM sync_state
            """, cancellationToken: ct));
        return new SyncStatus(row.LastSyncAt is null ? null : SqlText.ParseInstant(row.LastSyncAt), row.LastError, (int)row.Pending);
    }
}
