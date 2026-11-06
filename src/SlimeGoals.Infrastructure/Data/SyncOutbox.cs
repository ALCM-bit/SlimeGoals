using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Fila local de sincronização. Os repositórios chamam dentro da transação que grava o dado.</summary>
internal static class SyncOutbox
{
    public const string Goal = "goal";
    public const string Checkin = "checkin";

    /// <summary>
    /// Enfileira o registro como upsert. Sem efeito com a sincronização desligada; se já havia
    /// entrada, ela é substituída e mantém a posição.
    /// </summary>
    public static Task EnqueueAsync(SqliteConnection c, IDbTransaction tx, Guid recordId, string kind, CancellationToken ct) =>
        c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO sync_outbox (record_id, kind, op, base_seq)
            SELECT @Id, @Kind, 'upsert', COALESCE((SELECT seq FROM sync_records WHERE record_id = @Id), 0)
            WHERE (SELECT enabled FROM sync_state) = 1
            ON CONFLICT (record_id) DO UPDATE
              SET op = excluded.op, base_seq = excluded.base_seq, revision = revision + 1;
            """, new { Id = SqlText.Id(recordId), Kind = kind }, tx, cancellationToken: ct));
}
