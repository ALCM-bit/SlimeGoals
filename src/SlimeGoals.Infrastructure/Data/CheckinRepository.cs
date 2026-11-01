using Dapper;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ICheckinRepository"/>. Uma conexão por operação.</summary>
public sealed class CheckinRepository(SqliteConnectionFactory db) : ICheckinRepository
{
    public async Task AddAsync(Checkin checkin, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO checkins (id, goal_id, done_at, amount, note, updated_at, deleted_at)
            VALUES (@Id, @GoalId, @DoneAt, @Amount, @Note, @UpdatedAt, @DeletedAt)
            """, Row.FromDomain(checkin), cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Checkin>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, goal_id AS GoalId, done_at AS DoneAt, amount AS Amount, note AS Note,
                   updated_at AS UpdatedAt, deleted_at AS DeletedAt
            FROM checkins
            WHERE deleted_at IS NULL AND done_at >= @From AND done_at < @To
            ORDER BY done_at
            """, Range(fromUtc, toUtc), cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<int> SumAsync(Guid goalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COALESCE(SUM(amount), 0) FROM checkins
            WHERE goal_id = @GoalId AND deleted_at IS NULL AND done_at >= @From AND done_at < @To
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

    private static object Range(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        new { From = SqlText.Instant(fromUtc), To = SqlText.Instant(toUtc) };

    /// <summary>Linha crua de checkins, tudo como TEXT nos formatos da spec §8.</summary>
    private sealed class Row
    {
        public required string Id { get; init; }
        public required string GoalId { get; init; }
        public required string DoneAt { get; init; }
        public required int Amount { get; init; }
        public string? Note { get; init; }
        public required string UpdatedAt { get; init; }
        public string? DeletedAt { get; init; }

        public static Row FromDomain(Checkin c) => new()
        {
            Id = SqlText.Id(c.Id),
            GoalId = SqlText.Id(c.GoalId),
            DoneAt = SqlText.Instant(c.DoneAt),
            Amount = c.Amount,
            Note = c.Note,
            UpdatedAt = SqlText.Instant(c.UpdatedAt),
            DeletedAt = c.DeletedAt is { } d ? SqlText.Instant(d) : null,
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
        };
    }
}
