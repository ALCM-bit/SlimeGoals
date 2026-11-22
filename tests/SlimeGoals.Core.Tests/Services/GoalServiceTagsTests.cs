using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class GoalServiceTagsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryTagRepository _tags = new();
    private readonly InMemoryGoalRepository _goals;
    private readonly GoalService _service;

    public GoalServiceTagsTests()
    {
        _goals = new InMemoryGoalRepository(tags: _tags);
        _service = new GoalService(_goals, new InMemoryCheckinRepository(), new InMemoryTimerRepository(), _time, tags: _tags);
    }

    private static GoalDraft Draft(params string[]? tags) => GoalValidatorTests.Valid() with { Tags = tags };

    [Fact]
    public async Task Create_with_new_tag_creates_tag_and_link()
    {
        var goal = await _service.CreateAsync(Draft("  Saúde "));

        goal.Tags.Should().Equal("saúde");
        _tags.Tags.Should().ContainSingle().Which.Should().Match<Tag>(t =>
            t.Name == "saúde" && t.Color == GoalColor.Purple && t.UpdatedAt == T0 && t.DeletedAt == null);
        _tags.Links.Should().ContainSingle().Which.Should().Match<GoalTag>(l =>
            l.GoalId == goal.Id && l.TagName == "saúde" && l.UpdatedAt == T0 && l.RemovedAt == null);
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("saúde");
    }

    [Fact]
    public async Task Update_removing_tag_marks_link_removed()
    {
        var goal = await _service.CreateAsync(Draft("a", "b"));
        _time.Advance(TimeSpan.FromMinutes(5));

        var updated = await _service.UpdateAsync(goal.Id, Draft("a"));

        updated.Tags.Should().Equal("a");
        _tags.Links.Single(l => l.TagName == "b").Should().Match<GoalTag>(l =>
            l.RemovedAt == _time.GetUtcNow() && l.UpdatedAt == _time.GetUtcNow());
        _tags.Links.Single(l => l.TagName == "a").UpdatedAt.Should().Be(T0);
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("a");
    }

    [Fact]
    public async Task Update_with_null_tags_keeps_links()
    {
        var goal = await _service.CreateAsync(Draft("a"));
        var writes = _tags.Writes;
        _time.Advance(TimeSpan.FromMinutes(5));

        await _service.UpdateAsync(goal.Id, Draft(null) with { Title = "Outro" });

        _tags.Writes.Should().Be(writes);
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("a");
    }

    [Fact]
    public async Task Saving_goal_keeps_link_to_tag_not_yet_arrived_without_creating_row()
    {
        var goal = await _service.CreateAsync(Draft());
        var link = new GoalTag { Id = TagIds.ForLink(goal.Id, "fantasma"), GoalId = goal.Id, TagName = "fantasma", UpdatedAt = T0 };
        await _tags.SaveAsync(new TagChanges([], [link]));
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("fantasma");
        _time.Advance(TimeSpan.FromMinutes(5));

        await _service.UpdateAsync(goal.Id, Draft("fantasma", "nova"));

        _tags.Tags.Should().ContainSingle().Which.Name.Should().Be("nova");
        _tags.Links.Single(l => l.TagName == "fantasma").Should().Be(link);
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("fantasma", "nova");
    }

    [Fact]
    public async Task Invalid_tag_name_is_validation_error_on_Tags()
    {
        var act = () => _service.CreateAsync(Draft("   "));

        (await act.Should().ThrowAsync<GoalValidationException>())
            .Which.Errors.Should().ContainSingle().Which.Should().Be(new ValidationError(nameof(GoalDraft.Tags), "Dê um nome à tag."));
        _tags.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Adding_deleted_tag_to_other_goal_does_not_bring_back_hidden_links()
    {
        var hidden = await _service.CreateAsync(Draft());
        var other = await _service.CreateAsync(Draft());
        await _tags.SaveAsync(new TagChanges(
            [new Tag { Id = TagIds.ForTag("t"), Name = "t", Color = GoalColor.Green, UpdatedAt = T0.AddHours(1), DeletedAt = T0.AddHours(1) }],
            [new GoalTag { Id = TagIds.ForLink(hidden.Id, "t"), GoalId = hidden.Id, TagName = "t", UpdatedAt = T0 }]));
        _time.Advance(TimeSpan.FromHours(2));

        await _service.UpdateAsync(other.Id, Draft("t"));

        (await _goals.GetAsync(other.Id))!.Tags.Should().Equal("t");
        (await _goals.GetAsync(hidden.Id))!.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task Link_to_deleted_but_visible_tag_is_newer_than_the_deletion_even_with_a_lagging_clock()
    {
        // "t" foi excluída às T0+1h, mas outra meta a marcou depois (T0+2h): ela continua visível. O relógio daqui está em T0.
        var marker = await _service.CreateAsync(Draft());
        var goal = await _service.CreateAsync(Draft());
        var deletedAt = T0.AddHours(1);
        await _tags.SaveAsync(new TagChanges(
            [new Tag { Id = TagIds.ForTag("t"), Name = "t", Color = GoalColor.Green, UpdatedAt = deletedAt, DeletedAt = deletedAt }],
            [new GoalTag { Id = TagIds.ForLink(marker.Id, "t"), GoalId = marker.Id, TagName = "t", UpdatedAt = T0.AddHours(2) }]));

        var updated = await _service.UpdateAsync(goal.Id, Draft("t"));

        updated.Tags.Should().Equal("t");
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("t");
        _tags.Links.Single(l => l.GoalId == goal.Id).UpdatedAt.Should().Be(deletedAt.AddMilliseconds(1));
        _tags.Tags.Single().Should().Match<Tag>(t => t.DeletedAt == deletedAt && t.UpdatedAt == deletedAt, "a tag visível não é regravada");
    }
}

