using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Fila local de sincronização. Os repositórios chamam dentro da transação que grava o dado.</summary>
internal static class SyncOutbox
{
    public const string Goal = "goal";
    public const string Checkin = "checkin";

    private const string ReplaceOnConflict =
        " ON CONFLICT (record_id) DO UPDATE SET op = excluded.op, base_seq = excluded.base_seq, revision = revision + 1;";

    /// <summary>
    /// Enfileira o registro como upsert. Sem efeito com a sincronização desligada; se já havia
    /// entrada, ela é substituída e mantém a posição.
    /// </summary>
    public static Task EnqueueAsync(SqliteConnection c, IDbTransaction tx, Guid recordId, string kind, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT @Id, @Kind, 'upsert', COALESCE((SELECT seq FROM sync_records WHERE record_id = @Id), 0)
            WHERE (SELECT enabled FROM sync_state) = 1
            """ + ReplaceOnConflict, new { Id = SqlText.Id(recordId), Kind = kind }, tx, cancellationToken: ct));

    /// <summary>
    /// Leva a exclusão da meta e de seus registros para a fila. Chamar antes dos DELETE, com os registros
    /// ainda presentes. O que o servidor já conhece vira exclusão; o que nunca foi enviado só sai da fila.
    /// </summary>
    public static async Task EnqueueGoalDeletionAsync(SqliteConnection c, IDbTransaction tx, Guid goalId, CancellationToken ct)
    {
        var parameters = new { Id = SqlText.Id(goalId) };
        if (await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT enabled FROM sync_state", transaction: tx, cancellationToken: ct)) != 1)
            return;

        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT t.id, t.kind, 'delete', r.seq
            FROM (SELECT id, 'checkin' AS kind FROM checkins WHERE goal_id = @Id
                  UNION ALL
                  SELECT id, 'goal' AS kind FROM goals WHERE id = @Id) t
            JOIN sync_records r ON r.record_id = t.id
            WHERE 1
            """ + ReplaceOnConflict + """
            DELETE FROM sync_outbox
            WHERE record_id NOT IN (SELECT record_id FROM sync_records)
              AND (record_id = @Id OR record_id IN (SELECT id FROM checkins WHERE goal_id = @Id));
            INSERT OR IGNORE INTO sync_tombstones (goal_id) VALUES (@Id);
            DELETE FROM sync_pending WHERE goal_id = @Id;
            """, parameters, tx, cancellationToken: ct));
    }
}
