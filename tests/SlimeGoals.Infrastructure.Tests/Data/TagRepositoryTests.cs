using Dapper;
using SlimeGoals.Core;
using SlimeGoals.Core.Domain;
using SlimeGoals.Infrastructure.Data;

namespace SlimeGoals.Infrastructure.Tests.Data;

public class TagRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly TestDatabase _db = new();
    private TagRepository Tags => new(_db.Factory);
    private GoalRepository Goals => new(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static Tag NewTag(string name, GoalColor color = GoalColor.Pink) =>
        new() { Id = TagIds.ForTag(name), Name = name, Color = color, UpdatedAt = T0 };

    private static GoalTag NewLink(Guid goalId, string name) =>
        new() { Id = TagIds.ForLink(goalId, name), GoalId = goalId, TagName = name, UpdatedAt = T0 };

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(), Title = "Água", Recurrence = Recurrence.Daily, Target = 8, Icon = "water",
        Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0), ActiveUntil = new TimeOnly(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = T0, UpdatedAt = T0, Unit = "copos",
    };

    [Fact]
    public async Task Save_upserts_tags_and_links_and_queues_them_when_sync_on()
    {
        _db.Anchor.Execute("UPDATE sync_state SET enabled = 1");
        var goal = NewGoal();
        await Goals.AddAsync(goal);
        _db.Anchor.Execute("DELETE FROM sync_outbox");
        var tag = NewTag("saúde");
        var link = NewLink(goal.Id, "saúde");

        await Tags.SaveAsync(new TagChanges([tag], [link]));
        var removed = tag with { Color = GoalColor.Amber, UpdatedAt = T0.AddMinutes(1), DeletedAt = T0.AddMinutes(1) };
        var unlinked = link with { UpdatedAt = T0.AddMinutes(1), RemovedAt = T0.AddMinutes(1) };
        await Tags.SaveAsync(new TagChanges([removed], [unlinked]));

        (await Tags.ListTagsAsync()).Should().Contain(removed).And.HaveCount(4);
        (await Tags.ListLinksAsync()).Should().ContainSingle().Which.Should().Be(unlinked);
        var queue = _db.Anchor.Query("SELECT kind, op, revision FROM sync_outbox ORDER BY position").ToList();
        queue.Select(r => (string)r.kind).Should().Equal("tag", "goal_tag");
        queue.Select(r => (string)r.op).Should().AllBe("upsert");
        queue.Select(r => (long)r.revision).Should().AllBeEquivalentTo(2);
    }

    [Fact]
    public async Task Save_with_sync_off_queues_nothing()
    {
        await Tags.SaveAsync(new TagChanges([NewTag("saúde")], [NewLink(Guid.CreateVersion7(), "saúde")]));

        _db.Anchor.ExecuteScalar<long>("SELECT COUNT(*) FROM sync_outbox").Should().Be(0);
        (await Tags.ListLinksAsync()).Should().HaveCount(1);
    }

    [Fact]
    public async Task Save_twice_keeps_one_row_per_id()
    {
        var goal = Guid.CreateVersion7();
        var changes = new TagChanges([NewTag("saúde")], [NewLink(goal, "saúde")]);

        await Tags.SaveAsync(changes);
        await Tags.SaveAsync(changes);

        _db.Anchor.ExecuteScalar<long>("SELECT COUNT(*) FROM tags WHERE name = 'saúde'").Should().Be(1);
        _db.Anchor.ExecuteScalar<long>("SELECT COUNT(*) FROM goal_tags").Should().Be(1);
    }

    [Fact]
    public async Task Lists_include_the_default_tags()
    {
        (await Tags.ListTagsAsync()).Should().BeEquivalentTo(DefaultTags.All);
    }

    [Fact]
    public async Task Empty_changes_do_nothing()
    {
        TagChanges.Empty.IsEmpty.Should().BeTrue();
        await Tags.SaveAsync(TagChanges.Empty);
        (await Tags.ListTagsAsync()).Should().HaveCount(3);
    }

    [Fact]
    public async Task Goal_deletion_removes_its_links()
    {
        var goal = NewGoal();
        var other = NewGoal();
        await Goals.AddAsync(goal);
        await Goals.AddAsync(other);
        await Tags.SaveAsync(new TagChanges([], [NewLink(goal.Id, "saúde"), NewLink(other.Id, "saúde")]));

        await Goals.DeleteAsync(goal.Id);

        (await Tags.ListLinksAsync()).Should().ContainSingle().Which.GoalId.Should().Be(other.Id);
    }
}
