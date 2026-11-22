using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.Testing;

/// <summary>Implementação em memória de <see cref="ITagRepository"/>: upsert por id, como a tabela.</summary>
public sealed class InMemoryTagRepository : ITagRepository
{
    private readonly Dictionary<Guid, Tag> _tags = new();
    private readonly Dictionary<Guid, GoalTag> _links = new();

    /// <summary>Quantas vezes <see cref="SaveAsync"/> gravou algo (mudanças vazias não contam).</summary>
    public int Writes { get; private set; }

    public IReadOnlyList<Tag> Tags => [.. _tags.Values];

    public IReadOnlyList<GoalTag> Links => [.. _links.Values];

    public Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default) => Task.FromResult(Tags);

    public Task<IReadOnlyList<GoalTag>> ListLinksAsync(CancellationToken ct = default) => Task.FromResult(Links);

    public Task SaveAsync(TagChanges changes, CancellationToken ct = default)
    {
        Apply(changes);
        return Task.CompletedTask;
    }

    /// <summary>Grava as linhas (usado também pelo repositório de metas, que grava junto).</summary>
    public void Apply(TagChanges changes)
    {
        if (changes.IsEmpty) return;
        Writes++;
        foreach (var tag in changes.Tags) _tags[tag.Id] = tag;
        foreach (var link in changes.Links) _links[link.Id] = link;
    }

    /// <summary>Nomes visíveis das tags da meta, como a leitura do banco calcula.</summary>
    public IReadOnlyList<string> NamesFor(Guid goalId) =>
        TagVisibility.NamesFor(goalId, _links.Values, _tags.Values.ToDictionary(t => t.Name));
}
