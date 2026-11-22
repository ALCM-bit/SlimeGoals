using Dapper;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Adaptador SQLite da porta <see cref="ITagRepository"/>. Uma conexão por operação.</summary>
public sealed class TagRepository(SqliteConnectionFactory db) : ITagRepository
{
    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await RowReader.ReadValidAsync<TagRow, Tag>(c,
            new CommandDefinition(TagRows.SelectTags, cancellationToken: ct), "tags", r => r.ToDomain());
    }

    public async Task<IReadOnlyList<GoalTag>> ListLinksAsync(CancellationToken ct = default)
    {
        await using var c = db.Open();
        return await RowReader.ReadValidAsync<GoalTagRow, GoalTag>(c,
            new CommandDefinition(TagRows.SelectLinks, cancellationToken: ct), "goal_tags", r => r.ToDomain());
    }

    public async Task SaveAsync(TagChanges changes, CancellationToken ct = default)
    {
        if (changes.IsEmpty)
            return;
        await using var c = db.Open();
        await using var tx = await c.BeginTransactionAsync(ct);
        await TagRows.WriteAsync(c, tx, changes, ct);
        await tx.CommitAsync(ct);
    }
}
