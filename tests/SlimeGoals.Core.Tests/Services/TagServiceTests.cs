using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class TagServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryTagRepository _tags = new();
    private readonly InMemoryGoalRepository _goals;
    private readonly TagService _service;
    private readonly GoalService _goalService;

    public TagServiceTests()
    {
        _goals = new InMemoryGoalRepository(tags: _tags);
        _service = new TagService(_tags, _goals, _time);
        _goalService = new GoalService(_goals, new InMemoryCheckinRepository(), new InMemoryTimerRepository(), _time, tags: _tags);
    }

    private static Tag NewTag(string name, GoalColor color, DateTimeOffset at, DateTimeOffset? deletedAt = null) =>
        new() { Id = TagIds.ForTag(name), Name = name, Color = color, UpdatedAt = at, DeletedAt = deletedAt };

    private static GoalTag NewLink(Guid goal, string name, DateTimeOffset at, DateTimeOffset? removedAt = null) =>
        new() { Id = TagIds.ForLink(goal, name), GoalId = goal, TagName = name, UpdatedAt = at, RemovedAt = removedAt };

    private Task Seed(IEnumerable<Tag>? tags = null, IEnumerable<GoalTag>? links = null) =>
        _tags.SaveAsync(new TagChanges([.. tags ?? []], [.. links ?? []]));

    private async Task<Goal> GoalWith(params string[] tags) =>
        await _goalService.CreateAsync(GoalValidatorTests.Valid() with { Tags = tags });

    [Fact]
    public async Task Create_normalizes_and_picks_first_unused_color()
    {
        await Seed([NewTag("a", GoalColor.Purple, T0)]);

        var created = await _service.CreateAsync("  Saúde   Física ");

        created.Name.Should().Be("saúde física");
        created.Color.Should().Be(GoalColor.Pink);
        _tags.Tags.Should().ContainSingle(t => t.Name == "saúde física" && t.UpdatedAt == T0 && t.DeletedAt == null);
    }

    [Fact]
    public async Task Create_existing_returns_it_without_writing()
    {
        await Seed([NewTag("estudos", GoalColor.Green, T0.AddDays(-1))]);
        var writes = _tags.Writes;

        var created = await _service.CreateAsync("Estudos");

        created.Should().Be(new TagSummary("estudos", GoalColor.Green, 0, 0));
        _tags.Writes.Should().Be(writes);
    }

    [Fact]
    public async Task Create_deleted_reactivates_with_new_updated_at()
    {
        var late = T0.AddHours(1);
        await Seed([NewTag("estudos", GoalColor.Green, late, late)]);

        var created = await _service.CreateAsync("estudos");

        created.Color.Should().Be(GoalColor.Green);
        _tags.Tags.Should().ContainSingle().Which.Should().Match<Tag>(t =>
            t.DeletedAt == null && t.UpdatedAt == late.AddMilliseconds(1));
    }

    [Theory]
    [InlineData("   ", "Dê um nome à tag.")]
    [InlineData("abcdefghijklmnopqrstuvwxy", "A tag pode ter até 24 caracteres.")]
    public async Task Create_invalid_throws_with_message(string raw, string message)
    {
        var act = () => _service.CreateAsync(raw);

        (await act.Should().ThrowAsync<GoalValidationException>())
            .Which.Errors.Should().ContainSingle().Which.Should().Be(new ValidationError("Name", message));
    }

    [Fact]
    public async Task Change_color_bumps_updated_at_past_known_plus_1ms()
    {
        var future = T0.AddHours(2);
        await Seed([NewTag("estudos", GoalColor.Purple, future)]);

        await _service.ChangeColorAsync("Estudos", GoalColor.Amber);

        _tags.Tags.Should().ContainSingle().Which.Should().Match<Tag>(t =>
            t.Color == GoalColor.Amber && t.UpdatedAt == future.AddMilliseconds(1));
    }

    [Fact]
    public async Task Rename_moves_links_and_deletes_old_with_same_instant()
    {
        var goal = await GoalWith("velho");
        await Seed([NewTag("velho", GoalColor.Green, T0.AddDays(-1))]);
        _time.Advance(TimeSpan.FromHours(1));
        var now = _time.GetUtcNow();

        await _service.RenameAsync("velho", "Novo");

        var tags = _tags.Tags.ToDictionary(t => t.Name);
        tags["novo"].Should().Match<Tag>(t => t.Color == GoalColor.Green && t.DeletedAt == null && t.UpdatedAt == now);
        tags["velho"].DeletedAt.Should().Be(now);
        var links = _tags.Links.ToDictionary(l => l.TagName);
        links["velho"].RemovedAt.Should().Be(now);
        links["novo"].Should().Match<GoalTag>(l => l.RemovedAt == null && l.UpdatedAt == now && l.GoalId == goal.Id);
        (await _goals.GetAsync(goal.Id))!.Tags.Should().Equal("novo");
    }

    [Fact]
    public async Task Rename_into_existing_merges_and_keeps_target_color()
    {
        var a = await GoalWith("velho");
        var b = await GoalWith("velho", "estudos");
        await Seed([NewTag("velho", GoalColor.Green, T0.AddDays(-1)), NewTag("estudos", GoalColor.Pink, T0.AddDays(-1))]);

        (await _service.RenameNeedsMergeAsync("velho", "Estudos")).Should().BeTrue();
        _time.Advance(TimeSpan.FromHours(1));
        await _service.RenameAsync("velho", "Estudos");

        _tags.Tags.Single(t => t.Name == "estudos").Should().Match<Tag>(t => t.Color == GoalColor.Pink && t.DeletedAt == null);
        _tags.Tags.Single(t => t.Name == "velho").DeletedAt.Should().NotBeNull();
        (await _goals.GetAsync(a.Id))!.Tags.Should().Equal("estudos");
        (await _goals.GetAsync(b.Id))!.Tags.Should().Equal("estudos");
    }

    [Fact]
    public async Task Rename_to_same_normalized_name_does_nothing()
    {
        await GoalWith("estudos");
        await Seed([NewTag("estudos", GoalColor.Green, T0)]);
        var writes = _tags.Writes;

        await _service.RenameAsync("estudos", "  ESTUDOS ");

        _tags.Writes.Should().Be(writes);
        (await _service.RenameNeedsMergeAsync("estudos", "ESTUDOS")).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_removes_links_of_active_and_archived_goals_at_same_instant()
    {
        var active = await GoalWith("estudos");
        var archived = await GoalWith("estudos");
        await _goalService.ArchiveAsync(archived.Id);
        await Seed([NewTag("estudos", GoalColor.Green, T0)]);
        _time.Advance(TimeSpan.FromHours(1));
        var now = _time.GetUtcNow();

        await _service.DeleteAsync("Estudos");

        _tags.Tags.Single().DeletedAt.Should().Be(now);
        _tags.Links.Should().HaveCount(2).And.AllSatisfy(l => l.RemovedAt.Should().Be(now));
        (await _goals.GetAsync(active.Id))!.Tags.Should().BeEmpty();
        (await _goals.GetAsync(archived.Id))!.Tags.Should().BeEmpty();
        (await _service.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task List_counts_active_and_archived_separately()
    {
        await GoalWith("estudos", "saúde");
        var archived = await GoalWith("estudos");
        await _goalService.ArchiveAsync(archived.Id);
        await Seed([NewTag("saúde", GoalColor.Pink, T0), NewTag("estudos", GoalColor.Green, T0),
            NewTag("vazia", GoalColor.Amber, T0), NewTag("morta", GoalColor.Amber, T0, T0)]);

        var list = await _service.ListAsync();

        list.Should().Equal(
            new TagSummary("estudos", GoalColor.Green, 1, 1),
            new TagSummary("saúde", GoalColor.Pink, 1, 0),
            new TagSummary("vazia", GoalColor.Amber, 0, 0));
        (await _service.ColorsAsync()).Should().BeEquivalentTo(new Dictionary<string, GoalColor>
        {
            ["estudos"] = GoalColor.Green, ["saúde"] = GoalColor.Pink, ["vazia"] = GoalColor.Amber,
        });
    }

    [Fact]
    public async Task Goal_updated_at_never_changes()
    {
        var goal = await GoalWith("velho");
        await Seed([NewTag("velho", GoalColor.Green, T0)]);
        _time.Advance(TimeSpan.FromHours(1));

        await _service.RenameAsync("velho", "novo");
        await _service.ChangeColorAsync("novo", GoalColor.Pink);
        await _service.DeleteAsync("novo");

        (await _goals.GetAsync(goal.Id))!.UpdatedAt.Should().Be(goal.UpdatedAt);
    }
}
