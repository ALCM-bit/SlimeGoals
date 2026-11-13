using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Diagnostics;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ITimerRepository"/>: tabela active_timer, linha única (id = 1).</summary>
public sealed class TimerRepository(SqliteConnectionFactory db) : ITimerRepository
{
    public async Task<ActiveTimer?> GetAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        try
        {
            var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
                "SELECT goal_id AS GoalId, started_at AS StartedAt, stopped_at AS StoppedAt FROM active_timer WHERE id = 1",
                cancellationToken: ct));
            return row is null
                ? null
                : new ActiveTimer(Guid.ParseExact(row.GoalId, "D"), SqlText.ParseInstant(row.StartedAt),
                    row.StoppedAt is null ? null : SqlText.ParseInstant(row.StoppedAt));
        }
        catch (Exception e) when (RowReader.IsInvalidRow(e))
        {
            // Sem o início não há tempo a recuperar, e a linha ocuparia a vaga do único cronômetro para sempre.
            Trace.TraceError($"Cronômetro inválido descartado: {TraceText.Types(e)}");
            await c.ExecuteAsync(new CommandDefinition("DELETE FROM active_timer", cancellationToken: ct));
            return null;
        }
    }

    public async Task<bool> TryStartAsync(ActiveTimer timer, CancellationToken ct = default)
    {
        await using var c = db.Open();
        try
        {
            // INSERT OR IGNORE é atômico: dois processos iniciando juntos não criam dois cronômetros.
            return await c.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO active_timer (id, goal_id, started_at, stopped_at) VALUES (1, @GoalId, @StartedAt, @StoppedAt)",
                Params(timer), cancellationToken: ct)) == 1;
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == SqlText.ForeignKeyViolation)
        {
            throw new GoalNotFoundException(timer.GoalId); // a meta foi excluída depois de quem inicia lê-la
        }
    }

    public async Task SaveAsync(ActiveTimer timer, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition(
            "INSERT OR REPLACE INTO active_timer (id, goal_id, started_at, stopped_at) VALUES (1, @GoalId, @StartedAt, @StoppedAt)",
            Params(timer), cancellationToken: ct));
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM active_timer", cancellationToken: ct));
    }

    private static object Params(ActiveTimer t) => new
    {
        GoalId = SqlText.Id(t.GoalId),
        StartedAt = SqlText.Instant(t.StartedAt),
        StoppedAt = t.StoppedAt is { } s ? SqlText.Instant(s) : null,
    };

    private sealed class Row
    {
        public required string GoalId { get; init; }
        public required string StartedAt { get; init; }
        public string? StoppedAt { get; init; }
    }
}
