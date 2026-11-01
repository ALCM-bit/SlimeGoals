using Dapper;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="IGoalRepository"/>. Uma conexão por operação.</summary>
public sealed class GoalRepository(SqliteConnectionFactory db) : IGoalRepository
{
    private const string SelectColumns = """
        SELECT id AS Id, title AS Title, recurrence AS Recurrence, target AS Target, icon AS Icon,
               color AS Color, reminder_every_min AS ReminderEveryMin, active_from AS ActiveFrom,
               active_until AS ActiveUntil, active_days AS ActiveDays, start_date AS StartDate,
               end_date AS EndDate, created_at AS CreatedAt, updated_at AS UpdatedAt,
               archived_at AS ArchivedAt, deleted_at AS DeletedAt
        FROM goals
        """;

    public async Task AddAsync(Goal goal, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goals (id, title, recurrence, target, icon, color, reminder_every_min, active_from,
                               active_until, active_days, start_date, end_date, created_at, updated_at,
                               archived_at, deleted_at)
            VALUES (@Id, @Title, @Recurrence, @Target, @Icon, @Color, @ReminderEveryMin, @ActiveFrom,
                    @ActiveUntil, @ActiveDays, @StartDate, @EndDate, @CreatedAt, @UpdatedAt,
                    @ArchivedAt, @DeletedAt)
            """, GoalRow.FromDomain(goal), cancellationToken: ct));
    }

    public async Task UpdateAsync(Goal goal, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var affected = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE goals SET title = @Title, recurrence = @Recurrence, target = @Target, icon = @Icon,
                   color = @Color, reminder_every_min = @ReminderEveryMin, active_from = @ActiveFrom,
                   active_until = @ActiveUntil, active_days = @ActiveDays, start_date = @StartDate,
                   end_date = @EndDate, updated_at = @UpdatedAt,
                   archived_at = @ArchivedAt, deleted_at = @DeletedAt
            WHERE id = @Id
            """, GoalRow.FromDomain(goal), cancellationToken: ct));

        if (affected == 0)
            throw new GoalNotFoundException(goal.Id);
    }

    public async Task<Goal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var row = await c.QuerySingleOrDefaultAsync<GoalRow>(new CommandDefinition(
            SelectColumns + " WHERE id = @Id AND deleted_at IS NULL",
            new { Id = id.ToString("D") }, cancellationToken: ct));
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var rows = await c.QueryAsync<GoalRow>(new CommandDefinition(
            SelectColumns + """
             WHERE deleted_at IS NULL AND (@IncludeArchived OR archived_at IS NULL)
             ORDER BY created_at
            """, new { IncludeArchived = includeArchived }, cancellationToken: ct));
        return rows.Select(r => r.ToDomain()).ToList();
    }
}
