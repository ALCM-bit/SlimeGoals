using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Infrastructure.Data;

/// <summary>Linhas de <c>tags</c> e <c>goal_tags</c>: conversão para o domínio e gravação com a fila de sincronização.</summary>
internal static class TagRows
{
    /// <summary>Colunas de <see cref="TagRow"/>, sem filtro nem ordem.</summary>
    public const string TagColumns =
        "SELECT id AS Id, name AS Name, color AS Color, updated_at AS UpdatedAt, deleted_at AS DeletedAt FROM tags";

    /// <summary>Colunas de <see cref="GoalTagRow"/>, sem filtro nem ordem.</summary>
    public const string LinkColumns =
        "SELECT id AS Id, goal_id AS GoalId, tag_name AS TagName, updated_at AS UpdatedAt, removed_at AS RemovedAt FROM goal_tags";

    public const string SelectTags = TagColumns + " ORDER BY name";

    public const string SelectLinks = LinkColumns + " ORDER BY goal_id, tag_name";

    /// <summary>Upsert por <c>id</c> com os parâmetros de <see cref="TagRow"/>. Não mexe na fila.</summary>
    public const string UpsertTag = """
        INSERT INTO tags (id, name, color, updated_at, deleted_at)
        VALUES (@Id, @Name, @Color, @UpdatedAt, @DeletedAt)
        ON CONFLICT (id) DO UPDATE SET name = excluded.name, color = excluded.color,
            updated_at = excluded.updated_at, deleted_at = excluded.deleted_at
        """;

    /// <summary>Upsert por <c>id</c> com os parâmetros de <see cref="GoalTagRow"/>. Não mexe na fila.</summary>
    public const string UpsertLink = """
        INSERT INTO goal_tags (id, goal_id, tag_name, updated_at, removed_at)
        VALUES (@Id, @GoalId, @TagName, @UpdatedAt, @RemovedAt)
        ON CONFLICT (id) DO UPDATE SET goal_id = excluded.goal_id, tag_name = excluded.tag_name,
            updated_at = excluded.updated_at, removed_at = excluded.removed_at
        """;

    /// <summary>Upsert por <c>id</c> e enfileiramento de cada linha, dentro da transação do chamador.</summary>
    public static async Task WriteAsync(SqliteConnection c, IDbTransaction tx, TagChanges changes, CancellationToken ct)
    {
        foreach (var tag in changes.Tags)
        {
            await c.ExecuteAsync(new CommandDefinition(UpsertTag, TagRow.FromDomain(tag), tx, cancellationToken: ct));
            await SyncOutbox.EnqueueAsync(c, tx, tag.Id, SyncOutbox.Tag, ct);
        }

        foreach (var link in changes.Links)
        {
            await c.ExecuteAsync(new CommandDefinition(UpsertLink, GoalTagRow.FromDomain(link), tx, cancellationToken: ct));
            await SyncOutbox.EnqueueAsync(c, tx, link.Id, SyncOutbox.GoalTag, ct);
        }
    }
}

/// <summary>Linha crua de <c>tags</c>; cor como em <see cref="GoalRow"/>.</summary>
internal sealed class TagRow
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required string UpdatedAt { get; init; }
    public string? DeletedAt { get; init; }

    public static TagRow FromDomain(Tag t) => new()
    {
        Id = SqlText.Id(t.Id),
        Name = t.Name,
        Color = t.Color.ToString().ToLowerInvariant(),
        UpdatedAt = SqlText.Instant(t.UpdatedAt),
        DeletedAt = t.DeletedAt is { } d ? SqlText.Instant(d) : null,
    };

    public Tag ToDomain() => new()
    {
        Id = Guid.ParseExact(Id, "D"),
        Name = Name,
        Color = Color switch
        {
            "purple" => GoalColor.Purple,
            "pink" => GoalColor.Pink,
            "amber" => GoalColor.Amber,
            "green" => GoalColor.Green,
            _ => throw new FormatException($"Cor desconhecida no banco: '{Color}'."),
        },
        UpdatedAt = SqlText.ParseInstant(UpdatedAt),
        DeletedAt = DeletedAt is null ? null : SqlText.ParseInstant(DeletedAt),
    };
}

/// <summary>Linha crua de <c>goal_tags</c>.</summary>
internal sealed class GoalTagRow
{
    public required string Id { get; init; }
    public required string GoalId { get; init; }
    public required string TagName { get; init; }
    public required string UpdatedAt { get; init; }
    public string? RemovedAt { get; init; }

    public static GoalTagRow FromDomain(GoalTag l) => new()
    {
        Id = SqlText.Id(l.Id),
        GoalId = SqlText.Id(l.GoalId),
        TagName = l.TagName,
        UpdatedAt = SqlText.Instant(l.UpdatedAt),
        RemovedAt = l.RemovedAt is { } r ? SqlText.Instant(r) : null,
    };

    public GoalTag ToDomain() => new()
    {
        Id = Guid.ParseExact(Id, "D"),
        GoalId = Guid.ParseExact(GoalId, "D"),
        TagName = TagName,
        UpdatedAt = SqlText.ParseInstant(UpdatedAt),
        RemovedAt = RemovedAt is null ? null : SqlText.ParseInstant(RemovedAt),
    };
}
