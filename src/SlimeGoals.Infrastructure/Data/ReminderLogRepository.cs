using Dapper;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="IReminderLogRepository"/>. Uma conexão por operação.</summary>
public sealed class ReminderLogRepository(SqliteConnectionFactory db) : IReminderLogRepository
{
    public async Task AddAsync(ReminderLogEntry entry, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO reminder_log (id, goal_id, fired_at, response, responded_at)
            VALUES (@Id, @GoalId, @FiredAt, @Response, @RespondedAt)
            """, Row.FromDomain(entry), cancellationToken: ct));
    }

    public async Task<ReminderLogEntry?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, goal_id AS GoalId, fired_at AS FiredAt, response AS Response, responded_at AS RespondedAt
            FROM reminder_log WHERE id = @Id
            """, new { Id = SqlText.Id(id) }, cancellationToken: ct));
        return row?.ToDomain();
    }

    public async Task<bool> RespondAsync(Guid id, ReminderResponse response, DateTimeOffset respondedAt, CancellationToken ct = default)
    {
        await using var c = db.Open();
        // Só grava se ainda não há resposta: a segunda chamada não altera a primeira.
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE reminder_log SET response = @Response, responded_at = @RespondedAt
            WHERE id = @Id AND response IS NULL
            """, new
            {
                Id = SqlText.Id(id),
                Response = Row.ResponseText(response),
                RespondedAt = SqlText.Instant(respondedAt),
            }, cancellationToken: ct)) == 1;
    }

    public async Task<IReadOnlyList<ReminderLogEntry>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, goal_id AS GoalId, fired_at AS FiredAt, response AS Response, responded_at AS RespondedAt
            FROM reminder_log
            WHERE fired_at >= @From AND fired_at < @To
            ORDER BY fired_at
            """, new { From = SqlText.Instant(fromUtc), To = SqlText.Instant(toUtc) }, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ReminderLogEntry>> ListPendingAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, goal_id AS GoalId, fired_at AS FiredAt, response AS Response, responded_at AS RespondedAt
            FROM reminder_log
            WHERE response IS NULL
            ORDER BY fired_at
            """, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }

    /// <summary>Linha crua de reminder_log, tudo como TEXT nos formatos da spec §8.</summary>
    private sealed class Row
    {
        public required string Id { get; init; }
        public required string GoalId { get; init; }
        public required string FiredAt { get; init; }
        public string? Response { get; init; }
        public string? RespondedAt { get; init; }

        public static Row FromDomain(ReminderLogEntry e) => new()
        {
            Id = SqlText.Id(e.Id),
            GoalId = SqlText.Id(e.GoalId),
            FiredAt = SqlText.Instant(e.FiredAt),
            Response = e.Response is { } r ? ResponseText(r) : null,
            RespondedAt = e.RespondedAt is { } at ? SqlText.Instant(at) : null,
        };

        public ReminderLogEntry ToDomain() => new(
            Guid.ParseExact(Id, "D"),
            Guid.ParseExact(GoalId, "D"),
            SqlText.ParseInstant(FiredAt),
            Response is null ? null : ParseResponse(Response),
            RespondedAt is null ? null : SqlText.ParseInstant(RespondedAt));

        /// <summary>Texto gravado na coluna <c>response</c> (spec §8).</summary>
        public static string ResponseText(ReminderResponse response) => response switch
        {
            ReminderResponse.Done => "done",
            ReminderResponse.Snooze => "snooze",
            ReminderResponse.Ignore => "ignore",
            _ => throw new ArgumentOutOfRangeException(nameof(response), response, null),
        };

        private static ReminderResponse ParseResponse(string text) => text switch
        {
            "done" => ReminderResponse.Done,
            "snooze" => ReminderResponse.Snooze,
            "ignore" => ReminderResponse.Ignore,
            _ => throw new FormatException($"Resposta de lembrete desconhecida no banco: '{text}'."),
        };
    }
}
