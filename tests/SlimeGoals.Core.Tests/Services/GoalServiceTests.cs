using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Fakes;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Tests.Services;

public class GoalServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _repo = new();
    private GoalService Service => new(_repo, _time);

    [Fact]
    public async Task Create_assigns_id_timestamps_and_trims_title()
    {
        var goal = await Service.CreateAsync(GoalValidatorTests.Valid() with { Title = "  Ler 📚 ação  " });

        goal.Title.Should().Be("Ler 📚 ação");
        goal.Id.Should().NotBe(Guid.Empty);
        goal.CreatedAt.Should().Be(T0);
        goal.UpdatedAt.Should().Be(T0);
        (await _repo.GetAsync(goal.Id)).Should().Be(goal);
    }

    [Fact]
    public async Task Create_with_invalid_draft_throws_and_persists_nothing()
    {
        var act = () => Service.CreateAsync(GoalValidatorTests.Valid() with { Title = "" });
        (await act.Should().ThrowAsync<GoalValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.Field == "Title");
        _repo.Count.Should().Be(0);
    }

    [Fact]
    public async Task Update_keeps_created_at_and_bumps_updated_at()
    {
        var created = await Service.CreateAsync(GoalValidatorTests.Valid());
        _time.Advance(TimeSpan.FromHours(1));

        var updated = await Service.UpdateAsync(created.Id, GoalValidatorTests.Valid() with { Title = "Novo", Target = 3 });

        updated.Id.Should().Be(created.Id);
        updated.Title.Should().Be("Novo");
        updated.Target.Should().Be(3);
        updated.CreatedAt.Should().Be(T0);
        updated.UpdatedAt.Should().Be(T0.AddHours(1));
        (await _repo.GetAsync(created.Id)).Should().Be(updated);
    }

    [Fact]
    public async Task Update_unknown_id_throws_GoalNotFoundException()
    {
        var act = () => Service.UpdateAsync(Guid.NewGuid(), GoalValidatorTests.Valid());
        await act.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public async Task Update_with_invalid_draft_throws_validation_error()
    {
        var created = await Service.CreateAsync(GoalValidatorTests.Valid());
        var act = () => Service.UpdateAsync(created.Id, GoalValidatorTests.Valid() with { Target = 0 });
        await act.Should().ThrowAsync<GoalValidationException>();
    }

    [Fact]
    public async Task Archive_is_idempotent()
    {
        var goal = await Service.CreateAsync(GoalValidatorTests.Valid());
        await Service.ArchiveAsync(goal.Id);
        _time.Advance(TimeSpan.FromDays(1));
        await Service.ArchiveAsync(goal.Id);

        (await _repo.ListAsync(includeArchived: true)).Single().ArchivedAt.Should().Be(T0);
    }

    [Fact]
    public async Task List_hides_archived_by_default_and_deleted_always()
    {
        var active = await Service.CreateAsync(GoalValidatorTests.Valid() with { Title = "Ativa" });
        var archived = await Service.CreateAsync(GoalValidatorTests.Valid() with { Title = "Arquivada" });
        var deleted = await Service.CreateAsync(GoalValidatorTests.Valid() with { Title = "Excluída" });
        await Service.ArchiveAsync(archived.Id);
        await Service.DeleteAsync(deleted.Id);

        (await Service.ListAsync()).Select(g => g.Title).Should().Equal("Ativa");
        (await Service.ListAsync(includeArchived: true)).Select(g => g.Title).Should().BeEquivalentTo(["Ativa", "Arquivada"]);
        (await Service.GetAsync(deleted.Id)).Should().BeNull();
        (await Service.GetAsync(active.Id)).Should().NotBeNull();
    }
}

public class GoalServiceTimeNormalizationTests
{
    private readonly GoalService _service = new(new InMemoryGoalRepository(), new FakeTimeProvider());

    // Revisão final #3: o banco guarda HH:mm; segundos não podem sobreviver nem burlar "início < fim".
    [Fact]
    public async Task Seconds_are_truncated_before_validation_and_storage()
    {
        var sameMinute = () => _service.CreateAsync(GoalValidatorTests.Valid() with
            { ActiveFrom = new TimeOnly(9, 0, 10), ActiveUntil = new TimeOnly(9, 0, 50) });
        await sameMinute.Should().ThrowAsync<GoalValidationException>();

        var goal = await _service.CreateAsync(GoalValidatorTests.Valid() with
            { ActiveFrom = new TimeOnly(9, 0, 30), ActiveUntil = new TimeOnly(18, 0, 59) });
        goal.ActiveFrom.Should().Be(new TimeOnly(9, 0));
        goal.ActiveUntil.Should().Be(new TimeOnly(18, 0));
    }
}
