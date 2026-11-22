namespace SlimeGoals.Core.Domain;

/// <summary>
/// Quem aparece para o usuário. Excluir uma tag não apaga as ligações: elas ficam escondidas
/// enquanto forem anteriores à exclusão, e uma ligação mais nova traz a tag de volta.
/// </summary>
public static class TagVisibility
{
    /// <summary>A ligação vale se não foi removida e não é anterior (ou igual) à exclusão da tag.</summary>
    public static bool IsLinkVisible(GoalTag link, Tag? tag) =>
        link.RemovedAt is null && (tag?.DeletedAt is not { } deletedAt || link.UpdatedAt > deletedAt);

    /// <summary>A tag aparece se não foi excluída ou se alguma ligação ativa dela é mais nova que a exclusão.</summary>
    public static bool IsTagVisible(Tag tag, IEnumerable<GoalTag> linksOfTag) =>
        tag.DeletedAt is not { } deletedAt
        || linksOfTag.Any(l => l.RemovedAt is null && l.UpdatedAt > deletedAt);

    /// <summary>Nomes visíveis das tags de uma meta, em ordem ordinal.</summary>
    public static IReadOnlyList<string> NamesFor(
        Guid goalId, IEnumerable<GoalTag> links, IReadOnlyDictionary<string, Tag> tagsByName) =>
        [.. links
            .Where(l => l.GoalId == goalId && IsLinkVisible(l, tagsByName.GetValueOrDefault(l.TagName)))
            .Select(l => l.TagName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
}
