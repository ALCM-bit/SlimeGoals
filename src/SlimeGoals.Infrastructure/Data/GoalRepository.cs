using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="IGoalRepository"/>. Uma conexão por operação.</summary>
public sealed class GoalRepository(SqliteConnectionFactory db) : IGoalRepository
{
    internal const string SelectColumns = """
        SELECT id AS Id, title AS Title, recurrence AS Recurrence, target AS Target, measure AS Measure, unit AS Unit, icon AS Icon,
               color AS Color, reminder_every_min AS ReminderEveryMin, active_from AS ActiveFrom,
               active_until AS ActiveUntil, active_days AS ActiveDays, start_date AS StartDate,
               end_date AS EndDate, created_at AS CreatedAt, updated_at AS UpdatedAt,
               archived_at AS ArchivedAt, deleted_at AS DeletedAt
        FROM goals
        """;

    public Task AddAsync(Goal goal, CancellationToken ct = default) => AddAsync(goal, TagChanges.Empty, ct);

    public async Task AddAsync(Goal goal, TagChanges tagChanges, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO goals (id, title, recurrence, target, measure, unit, icon, color, reminder_every_min, active_from,
                               active_until, active_days, start_date, end_date, created_at, updated_at,
                               archived_at, deleted_at)
            VALUES (@Id, @Title, @Recurrence, @Target, @Measure, @Unit, @Icon, @Color, @ReminderEveryMin, @ActiveFrom,
                    @ActiveUntil, @ActiveDays, @StartDate, @EndDate, @CreatedAt, @UpdatedAt,
                    @ArchivedAt, @DeletedAt)
            """, GoalRow.FromDomain(goal), tx, cancellationToken: ct));
        await SyncOutbox.EnqueueAsync(c, tx, goal.Id, SyncOutbox.Goal, ct);
        await TagRows.WriteAsync(c, tx, tagChanges, ct);
        await tx.CommitAsync(ct);
    }

    public Task UpdateAsync(Goal goal, CancellationToken ct = default) => UpdateAsync(goal, TagChanges.Empty, ct);

    public async Task UpdateAsync(Goal goal, TagChanges tagChanges, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        var affected = await c.ExecuteAsync(new CommandDefinition("""
            UPDATE goals SET title = @Title, recurrence = @Recurrence, target = @Target, measure = @Measure, unit = @Unit, icon = @Icon,
                   color = @Color, reminder_every_min = @ReminderEveryMin, active_from = @ActiveFrom,
                   active_until = @ActiveUntil, active_days = @ActiveDays, start_date = @StartDate,
                   end_date = @EndDate, updated_at = @UpdatedAt,
                   archived_at = @ArchivedAt, deleted_at = @DeletedAt
            WHERE id = @Id
            """, GoalRow.FromDomain(goal), tx, cancellationToken: ct));

        if (affected == 0)
            throw new GoalNotFoundException(goal.Id);
        await SyncOutbox.EnqueueAsync(c, tx, goal.Id, SyncOutbox.Goal, ct);
        await TagRows.WriteAsync(c, tx, tagChanges, ct);
        await tx.CommitAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await SyncOutbox.EnqueueGoalDeletionAsync(c, tx, id, ct);
        var affected = await GoalDeletion.DeleteRowsAsync(c, tx, id, ct);

        if (affected == 0)
            throw new GoalNotFoundException(id);
        await tx.CommitAsync(ct);
    }

    public async Task<Goal?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var c = db.Open();
        try
        {
            var row = await c.QuerySingleOrDefaultAsync<GoalRow>(new CommandDefinition(
                SelectColumns + " WHERE id = @Id AND deleted_at IS NULL",
                new { Id = id.ToString("D") }, cancellationToken: ct));
            if (row is null)
                return null;
            var (tags, links) = await ReadTagsAsync(c, ct);
            return row.ToDomain() with { Tags = VisibleNames(id, links, tags) };
        }
        catch (DataException e)
        {
            // Tipo errado numa coluna: o Dapper embrulha; quem lê por id só precisa saber que a linha é inválida.
            throw new FormatException($"Linha inválida em goals: {id}.", e);
        }
    }

    public async Task<IReadOnlyList<Goal>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        await using var c = db.Open();
        var goals = await RowReader.ReadValidAsync<GoalRow, Goal>(c, new CommandDefinition(
            SelectColumns + """
             WHERE deleted_at IS NULL AND (@IncludeArchived OR archived_at IS NULL)
             ORDER BY created_at
            """, new { IncludeArchived = includeArchived }, cancellationToken: ct), "goals", r => r.ToDomain());
        if (goals.Count == 0)
            return goals;

        var (tags, links) = await ReadTagsAsync(c, ct);
        var linksByGoal = links.ToLookup(l => l.GoalId);
        return [.. goals.Select(g => g with { Tags = VisibleNames(g.Id, linksByGoal[g.Id], tags) })];
    }

    // Sem tags, a mesma lista vazia compartilhada de Goal.Tags: a igualdade de record compara a lista por referência.
    private static IReadOnlyList<string> VisibleNames(
        Guid goalId, IEnumerable<GoalTag> links, IReadOnlyDictionary<string, Tag> tags) =>
        TagVisibility.NamesFor(goalId, links, tags) is { Count: > 0 } names ? names : [];

    /// <summary>Uma consulta de tags e uma de ligações por chamada, nunca uma por meta.</summary>
    private static async Task<(IReadOnlyDictionary<string, Tag> Tags, List<GoalTag> Links)> ReadTagsAsync(
        SqliteConnection c, CancellationToken ct)
    {
        var tags = (await RowReader.ReadValidAsync<TagRow, Tag>(c,
            new CommandDefinition(TagRows.SelectTags, cancellationToken: ct), "tags", r => r.ToDomain()))
            .ToDictionary(t => t.Name);
        var links = await RowReader.ReadValidAsync<GoalTagRow, GoalTag>(c,
            new CommandDefinition(TagRows.SelectLinks, cancellationToken: ct), "goal_tags", r => r.ToDomain());
        return (tags, links);
    }
}
