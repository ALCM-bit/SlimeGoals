using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ICheckinRepository"/>. Uma conexão por operação.</summary>
public sealed class CheckinRepository(SqliteConnectionFactory db) : ICheckinRepository
{
    public async Task AddAsync(Checkin checkin, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            await InsertAsync(c, tx, checkin, ct);
            await tx.CommitAsync(ct);
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == SqlText.ForeignKeyViolation)
        {
            throw new GoalNotFoundException(checkin.GoalId); // a meta foi excluída depois de quem registra lê-la
        }
    }

    /// <summary>Grava o registro e o põe na fila da sincronização, dentro da transação de quem chama.</summary>
    internal static async Task InsertAsync(SqliteConnection c, IDbTransaction tx, Checkin checkin, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO checkins (id, goal_id, done_at, amount, note, updated_at, deleted_at, compensation_period, compensation_seen)
            VALUES (@Id, @GoalId, @DoneAt, @Amount, @Note, @UpdatedAt, @DeletedAt, @CompensationPeriod, @CompensationSeen)
            """, Row.FromDomain(checkin), tx, cancellationToken: ct));
        await SyncOutbox.EnqueueAsync(c, tx, checkin.Id, SyncOutbox.Checkin, ct);
    }

    public async Task<Checkin?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var rows = await RowReader.ReadValidAsync<Row, Checkin>(c, new CommandDefinition(
            SelectColumns + " WHERE id = @Id AND deleted_at IS NULL", new { Id = SqlText.Id(id) }, cancellationToken: ct),
            "checkins", r => r.ToDomain());
        return rows.SingleOrDefault();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        if (!await DeleteRowAsync(c, tx, id, ct))
            return false;
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<bool> ReplaceAsync(Guid id, Checkin replacement, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        try
        {
            if (!await DeleteRowAsync(c, tx, id, ct))
                return false;
            await InsertAsync(c, tx, replacement, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (SqliteException e) when (e.SqliteExtendedErrorCode == SqlText.ForeignKeyViolation)
        {
            throw new GoalNotFoundException(replacement.GoalId);
        }
    }

    /// <summary>Apaga a linha e põe a exclusão na fila, na transação de quem chama. false se a linha não existia.</summary>
    private static async Task<bool> DeleteRowAsync(SqliteConnection c, IDbTransaction tx, Guid id, CancellationToken ct)
    {
        var removed = await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM checkins WHERE id = @Id", new { Id = SqlText.Id(id) }, tx, cancellationToken: ct));
        if (removed == 0)
            return false;
        await SyncOutbox.EnqueueDeletionAsync(c, tx, id, SyncOutbox.Checkin, ct);
        return true;
    }

    public async Task<IReadOnlyList<string>> RecentNotesAsync(Guid goalId, int limit, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var notes = await c.QueryAsync<string>(new CommandDefinition("""
            SELECT note FROM checkins
            WHERE goal_id = @GoalId AND note IS NOT NULL AND deleted_at IS NULL
            GROUP BY note
            ORDER BY MAX(done_at) DESC
            LIMIT @Limit
            """, new { GoalId = SqlText.Id(goalId), Limit = limit }, cancellationToken: ct));
        return notes.ToList();
    }

    public async Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await RowReader.ReadValidAsync<Row, Checkin>(c, new CommandDefinition(SelectColumns + """
             WHERE deleted_at IS NULL AND superseded = 0 AND done_at >= @From AND done_at < @To
            ORDER BY done_at
            """, Range(fromUtc, toUtc), cancellationToken: ct), "checkins", r => r.ToDomain());
    }

    public async Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(SUM(amount), 0) FROM checkins
            WHERE goal_id = @GoalId AND deleted_at IS NULL AND superseded = 0 AND done_at >= @From AND done_at < @To
            """, new { GoalId = SqlText.Id(goalId), From = SqlText.Instant(fromUtc), To = SqlText.Instant(toUtc) },
            cancellationToken: ct));
    }

    public async Task<bool> HasAnyAsync(Guid goalId, CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM checkins WHERE goal_id = @GoalId AND deleted_at IS NULL)",
            new { GoalId = SqlText.Id(goalId) }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> CompensationIdsAsync(Guid goalId, DateOnly period, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var ids = await c.QueryAsync<string>(new CommandDefinition("""
            SELECT id FROM checkins
            WHERE goal_id = @GoalId AND compensation_period = @Period AND deleted_at IS NULL
            ORDER BY done_at, id
            """, new { GoalId = SqlText.Id(goalId), Period = SqlText.Date(period) }, cancellationToken: ct));
        return ids.Select(i => Guid.ParseExact(i, "D")).ToList();
    }

    private const string SelectColumns = """
        SELECT id AS Id, goal_id AS GoalId, done_at AS DoneAt, amount AS Amount, note AS Note,
               updated_at AS UpdatedAt, deleted_at AS DeletedAt,
               compensation_period AS CompensationPeriod, compensation_seen AS CompensationSeen
        FROM checkins
        """;

    private static object Range(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        new { From = SqlText.Instant(fromUtc), To = SqlText.Instant(toUtc) };

    /// <summary>Linha crua de checkins, tudo como TEXT nos formatos da spec §8.</summary>
    internal sealed class Row
    {
        public required string Id { get; init; }
        public required string GoalId { get; init; }
        public required string DoneAt { get; init; }
        public required int Amount { get; init; }
        public string? Note { get; init; }
        public required string UpdatedAt { get; init; }
        public string? DeletedAt { get; init; }
        public string? CompensationPeriod { get; init; }
        public string? CompensationSeen { get; init; }

        public static Row FromDomain(Checkin c) => new()
        {
            Id = SqlText.Id(c.Id),
            GoalId = SqlText.Id(c.GoalId),
            DoneAt = SqlText.Instant(c.DoneAt),
            Amount = c.Amount,
            Note = c.Note,
            UpdatedAt = SqlText.Instant(c.UpdatedAt),
            DeletedAt = c.DeletedAt is { } d ? SqlText.Instant(d) : null,
            CompensationPeriod = c.Compensation is { } k ? SqlText.Date(k.Period) : null,
            CompensationSeen = c.Compensation is { } s ? SqlText.IdList(s.Seen) : null,
        };

        public Checkin ToDomain() => new()
        {
            Id = Guid.ParseExact(Id, "D"),
            GoalId = Guid.ParseExact(GoalId, "D"),
            DoneAt = SqlText.ParseInstant(DoneAt),
            Amount = Amount,
            Note = Note,
            UpdatedAt = SqlText.ParseInstant(UpdatedAt),
            DeletedAt = DeletedAt is null ? null : SqlText.ParseInstant(DeletedAt),
            Compensation = CompensationPeriod is null
                ? null
                : new Compensation(SqlText.ParseDate(CompensationPeriod), SqlText.ParseIdList(CompensationSeen)),
        };
    }
}
