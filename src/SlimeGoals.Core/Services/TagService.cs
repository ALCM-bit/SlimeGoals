using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Services;

/// <summary>
/// Casos de uso das tags: criar, trocar a cor, renomear (juntando se o nome já existe) e excluir.
/// Nenhuma operação toca nas metas: o <c>updated_at</c> delas fica como está.
/// A visibilidade nunca é gravada: excluir marca <c>DeletedAt</c> na tag e <c>RemovedAt</c> nas ligações, no mesmo instante.
/// </summary>
public sealed class TagService(ITagRepository tags, IGoalRepository goals, TimeProvider time, DataChangeNotifier? changes = null)
{
    private static readonly GoalColor[] Palette = [GoalColor.Purple, GoalColor.Pink, GoalColor.Amber, GoalColor.Green];

    /// <summary>As tags visíveis em ordem ordinal, com as contagens de metas ativas e arquivadas.</summary>
    public async Task<IReadOnlyList<TagSummary>> ListAsync(CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        var goalList = await goals.ListAsync(includeArchived: true, ct);
        return [.. state.Visible.Values.OrderBy(t => t.Name, StringComparer.Ordinal).Select(t => Summarize(t, state, goalList))];
    }

    /// <summary>Cor de cada tag visível. Quem chama usa Purple para um nome ausente.</summary>
    public async Task<IReadOnlyDictionary<string, GoalColor>> ColorsAsync(CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        return state.Visible.ToDictionary(p => p.Key, p => p.Value.Color, StringComparer.Ordinal);
    }

    /// <summary>Cria a tag, reativa uma excluída ou devolve a existente sem gravar.</summary>
    public async Task<TagSummary> CreateAsync(string rawName, CancellationToken ct = default)
    {
        var name = NormalizeValid(rawName);
        var state = await LoadAsync(ct);
        if (!state.Visible.ContainsKey(name))
        {
            var now = time.GetUtcNow();
            if (state.Tags.TryGetValue(name, out var old))
            {
                var hidden = state.HiddenLinksOf(old).Select(l => RemoveAt(l, Next(now, l.UpdatedAt)));
                await SaveAsync(new TagChanges([old with { DeletedAt = null, UpdatedAt = Next(now, old.UpdatedAt) }], [.. hidden]), ct);
            }
            else
            {
                var tag = NewTag(name, PickColor(state.Visible.Values.Select(t => t.Color), state.Visible.Count), now);
                await SaveAsync(new TagChanges([tag], []), ct);
            }
        }

        return (await ListAsync(ct)).Single(s => s.Name == name);
    }

    public async Task ChangeColorAsync(string name, GoalColor color, CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        if (!state.Visible.TryGetValue(TagName.Normalize(name), out var tag) || tag.Color == color)
            return;
        await SaveAsync(new TagChanges([tag with { Color = color, UpdatedAt = Next(time.GetUtcNow(), tag.UpdatedAt) }], []), ct);
    }

    /// <summary>O novo nome já é de uma tag visível: renomear junta as duas, e a tela pede confirmação.</summary>
    public async Task<bool> RenameNeedsMergeAsync(string oldName, string rawNewName, CancellationToken ct = default)
    {
        var newName = NormalizeValid(rawNewName);
        var state = await LoadAsync(ct);
        return newName != TagName.Normalize(oldName) && state.Visible.ContainsKey(newName);
    }

    /// <summary>
    /// Cria ou reativa a tag nova, passa as ligações para ela e exclui a antiga, tudo no mesmo instante.
    /// Se o nome novo já existe, as metas ficam com a tag existente, que mantém a cor.
    /// </summary>
    public async Task RenameAsync(string oldName, string rawNewName, CancellationToken ct = default)
    {
        var newName = NormalizeValid(rawNewName);
        var oldNormalized = TagName.Normalize(oldName);
        var state = await LoadAsync(ct);
        if (newName == oldNormalized || !state.Visible.TryGetValue(oldNormalized, out var oldTag))
            return;

        var oldLinks = state.VisibleLinksOf(oldTag);
        state.Tags.TryGetValue(newName, out var target);
        var known = oldLinks.Select(l => l.UpdatedAt).Append(oldTag.UpdatedAt);
        if (target is not null)
            known = known.Append(target.UpdatedAt).Concat(state.LinksOf(newName).Select(l => l.UpdatedAt));
        var instant = Next(time.GetUtcNow(), known.Max());

        var changedTags = new List<Tag> { oldTag with { DeletedAt = instant, UpdatedAt = instant } };
        if (!state.Visible.ContainsKey(newName))
            changedTags.Add((target ?? NewTag(newName, oldTag.Color, instant)) with { DeletedAt = null, UpdatedAt = instant });

        var changedLinks = new List<GoalTag>();
        if (target is not null && !state.Visible.ContainsKey(newName))
        {
            var moving = oldLinks.Select(l => l.GoalId).ToHashSet();
            changedLinks.AddRange(state.HiddenLinksOf(target).Where(l => !moving.Contains(l.GoalId)).Select(l => RemoveAt(l, instant)));
        }

        foreach (var link in oldLinks)
        {
            changedLinks.Add(link with { RemovedAt = instant, UpdatedAt = instant });
            var existing = state.LinksOf(newName).FirstOrDefault(l => l.GoalId == link.GoalId);
            var alreadyVisible = existing is not null && state.Visible.ContainsKey(newName) && TagVisibility.IsLinkVisible(existing, target);
            if (!alreadyVisible)
                changedLinks.Add(NewLink(link.GoalId, newName, instant));
        }

        await SaveAsync(new TagChanges(changedTags, changedLinks), ct);
    }

    /// <summary>Exclui a tag e tira as ligações dela, de metas ativas e arquivadas, no mesmo instante.</summary>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var state = await LoadAsync(ct);
        if (!state.Visible.TryGetValue(TagName.Normalize(name), out var tag))
            return;

        var links = state.VisibleLinksOf(tag);
        var instant = Next(time.GetUtcNow(), links.Select(l => l.UpdatedAt).Append(tag.UpdatedAt).Max());
        await SaveAsync(new TagChanges(
            [tag with { DeletedAt = instant, UpdatedAt = instant }],
            [.. links.Select(l => l with { RemovedAt = instant, UpdatedAt = instant })]), ct);
    }

    /// <summary>A primeira cor da paleta sem uso; com todas em uso, a da posição <c>visíveis mod 4</c>.</summary>
    internal static GoalColor PickColor(IEnumerable<GoalColor> inUse, int visibleCount)
    {
        var used = inUse.ToHashSet();
        foreach (var color in Palette)
            if (!used.Contains(color))
                return color;
        return Palette[visibleCount % Palette.Length];
    }

    /// <summary>Marca a ligação como removida no instante dado (a data de edição acompanha).</summary>
    internal static GoalTag RemoveAt(GoalTag link, DateTimeOffset at) => link with { RemovedAt = at, UpdatedAt = at };

    internal static Tag NewTag(string name, GoalColor color, DateTimeOffset at) =>
        new() { Id = TagIds.ForTag(name), Name = name, Color = color, UpdatedAt = at };

    internal static GoalTag NewLink(Guid goalId, string name, DateTimeOffset at) =>
        new() { Id = TagIds.ForLink(goalId, name), GoalId = goalId, TagName = name, UpdatedAt = at };

    /// <summary>Data de edição: a de agora, ou 1 ms depois da conhecida se o relógio estiver atrasado.</summary>
    internal static DateTimeOffset Next(DateTimeOffset now, DateTimeOffset known)
    {
        var after = known.AddMilliseconds(1);
        return now > after ? now : after;
    }

    internal static string NormalizeValid(string raw)
    {
        var name = TagName.Normalize(raw);
        if (TagName.Validate(name) is { } error)
            throw new GoalValidationException([new ValidationError("Name", error)]);
        return name;
    }

    private async Task SaveAsync(TagChanges toSave, CancellationToken ct)
    {
        await tags.SaveAsync(toSave, ct);
        changes?.NotifyChanged();
    }

    private async Task<TagState> LoadAsync(CancellationToken ct) =>
        new(await tags.ListTagsAsync(ct), await tags.ListLinksAsync(ct));

    private static TagSummary Summarize(Tag tag, TagState state, IReadOnlyList<Goal> goalList)
    {
        var linked = state.VisibleLinksOf(tag).Select(l => l.GoalId).ToHashSet();
        var used = goalList.Where(g => linked.Contains(g.Id)).ToList();
        return new TagSummary(tag.Name, tag.Color, used.Count(g => g.ArchivedAt is null), used.Count(g => g.ArchivedAt is not null));
    }

    /// <summary>Tags e ligações lidas juntas, com a visibilidade já calculada.</summary>
    internal sealed class TagState
    {
        private readonly ILookup<string, GoalTag> _links;

        public TagState(IReadOnlyList<Tag> allTags, IReadOnlyList<GoalTag> allLinks)
        {
            Tags = allTags.ToDictionary(t => t.Name, StringComparer.Ordinal);
            _links = allLinks.ToLookup(l => l.TagName, StringComparer.Ordinal);
            Visible = Tags.Where(p => TagVisibility.IsTagVisible(p.Value, _links[p.Key]))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            Links = allLinks;
        }

        public IReadOnlyDictionary<string, Tag> Tags { get; }

        public IReadOnlyDictionary<string, Tag> Visible { get; }

        public IReadOnlyList<GoalTag> Links { get; }

        public IEnumerable<GoalTag> LinksOf(string name) => _links[name];

        /// <summary>Ligações não removidas que só a exclusão da tag esconde; reativar a tag as traria de volta.</summary>
        public List<GoalTag> HiddenLinksOf(Tag tag) =>
            [.. _links[tag.Name].Where(l => l.RemovedAt is null && !TagVisibility.IsLinkVisible(l, tag))];

        public List<GoalTag> VisibleLinksOf(Tag tag) =>
            [.. _links[tag.Name].Where(l => TagVisibility.IsLinkVisible(l, tag))];
    }
}
