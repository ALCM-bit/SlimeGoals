using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class TagVisibilityTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid GoalA = Guid.Parse("0193a000-0000-7000-8000-00000000000a");
    private static readonly Guid GoalB = Guid.Parse("0193a000-0000-7000-8000-00000000000b");

    private static Tag MakeTag(string name, DateTimeOffset? deletedAt = null) => new()
    {
        Id = TagIds.ForTag(name),
        Name = name,
        Color = GoalColor.Purple,
        UpdatedAt = T0,
        DeletedAt = deletedAt,
    };

    private static GoalTag Link(Guid goal, string name, DateTimeOffset updatedAt, DateTimeOffset? removedAt = null) => new()
    {
        Id = TagIds.ForLink(goal, name),
        GoalId = goal,
        TagName = name,
        UpdatedAt = updatedAt,
        RemovedAt = removedAt,
    };

    [Fact]
    public void Removed_link_is_hidden() =>
        TagVisibility.IsLinkVisible(Link(GoalA, "estudos", T0, T0), MakeTag("estudos")).Should().BeFalse();

    [Fact]
    public void Link_without_tag_row_is_visible() =>
        TagVisibility.IsLinkVisible(Link(GoalA, "estudos", T0), null).Should().BeTrue();

    [Fact]
    public void Link_older_or_equal_to_deletion_is_hidden()
    {
        var tag = MakeTag("estudos", T0);
        TagVisibility.IsLinkVisible(Link(GoalA, "estudos", T0.AddSeconds(-1)), tag).Should().BeFalse();
        TagVisibility.IsLinkVisible(Link(GoalA, "estudos", T0), tag).Should().BeFalse();
    }

    [Fact]
    public void Link_newer_than_deletion_is_visible_and_revives_tag()
    {
        var tag = MakeTag("estudos", T0);
        var link = Link(GoalA, "estudos", T0.AddMilliseconds(1));
        TagVisibility.IsLinkVisible(link, tag).Should().BeTrue();
        TagVisibility.IsTagVisible(tag, [link]).Should().BeTrue();
    }

    [Fact]
    public void Deleted_tag_without_newer_link_is_not_visible()
    {
        var tag = MakeTag("estudos", T0);
        TagVisibility.IsTagVisible(tag, [Link(GoalA, "estudos", T0)]).Should().BeFalse();
        TagVisibility.IsTagVisible(tag, [Link(GoalA, "estudos", T0.AddSeconds(5), T0.AddSeconds(6))]).Should().BeFalse();
        TagVisibility.IsTagVisible(MakeTag("estudos"), []).Should().BeTrue();
    }

    [Fact]
    public void NamesFor_is_sorted_and_only_of_that_goal()
    {
        var tags = new Dictionary<string, Tag> { ["estudos"] = MakeTag("estudos") };
        var links = new[]
        {
            Link(GoalA, "tarefas", T0),
            Link(GoalA, "estudos", T0),
            Link(GoalB, "medicamentos", T0),
            Link(GoalA, "antiga", T0, T0.AddSeconds(1)),
        };
        TagVisibility.NamesFor(GoalA, links, tags).Should().Equal("estudos", "tarefas");
    }
}
