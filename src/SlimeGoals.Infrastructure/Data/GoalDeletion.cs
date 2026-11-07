using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Apaga a meta e o que depende dela, sem tocar na fila de sincronização.</summary>
internal static class GoalDeletion
{
    /// <summary>Devolve quantas metas foram apagadas (0 se ela não existia).</summary>
    public static async Task<int> DeleteRowsAsync(SqliteConnection c, IDbTransaction tx, Guid goalId, CancellationToken ct)
    {
        var parameters = new { Id = SqlText.Id(goalId) };
        // Filhas antes da meta: as chaves estrangeiras estão ligadas e não há ON DELETE CASCADE.
        await c.ExecuteAsync(new CommandDefinition("""
            DELETE FROM active_timer WHERE goal_id = @Id;
            DELETE FROM checkins WHERE goal_id = @Id;
            DELETE FROM reminder_log WHERE goal_id = @Id;
            """, parameters, tx, cancellationToken: ct));
        return await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM goals WHERE id = @Id", parameters, tx, cancellationToken: ct));
    }
}
