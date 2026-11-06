using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class GoalServiceMeasureTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly InMemoryGoalRepository _goals;
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();

    public GoalServiceMeasureTests() => _goals = new InMemoryGoalRepository(_checkins, timers: _timers);

    private GoalService Service => new(_goals, _checkins, _timers, _time);
    private CheckinService Checkins => new(_goals, _checkins, _time);

    [Fact]
    public async Task Measure_change_is_refused_after_first_record()
    {
        var goal = await Service.CreateAsync(GoalValidatorTests.Valid());
        await Checkins.AdjustAsync(goal.Id, 1);

        (await Service.HasRecordsAsync(goal.Id)).Should().BeTrue();
        var act = () => Service.UpdateAsync(goal.Id, GoalValidatorTests.Valid() with { Measure = Measure.Duration, Target = 60 });

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle(e => e.Field == "Measure" && e.Message == "A medida não muda depois do primeiro registro.");
        (await Service.UpdateAsync(goal.Id, GoalValidatorTests.Valid() with { Unit = "copos" })).Unit.Should().Be("copos");
    }

    [Fact]
    public async Task Measure_change_is_allowed_without_records()
    {
        var goal = await Service.CreateAsync(GoalValidatorTests.Valid());

        (await Service.HasRecordsAsync(goal.Id)).Should().BeFalse();
        (await Service.UpdateAsync(goal.Id, GoalValidatorTests.Valid() with { Measure = Measure.Duration, Target = 60 }))
            .Measure.Should().Be(Measure.Duration);
    }

    [Fact]
    public async Task Archive_and_delete_clear_the_timer_of_that_goal_only()
    {
        var a = await Service.CreateAsync(GoalValidatorTests.Valid());
        var b = await Service.CreateAsync(GoalValidatorTests.Valid());

        await _timers.TryStartAsync(new ActiveTimer(a.Id, _time.GetUtcNow()));
        await Service.ArchiveAsync(b.Id);
        (await _timers.GetAsync()).Should().NotBeNull();
        await Service.ArchiveAsync(a.Id);
        (await _timers.GetAsync()).Should().BeNull();

        var c = await Service.CreateAsync(GoalValidatorTests.Valid());
        await _timers.TryStartAsync(new ActiveTimer(c.Id, _time.GetUtcNow()));
        await Service.DeleteAsync(c.Id);
        (await _timers.GetAsync()).Should().BeNull();

        var d = await Service.CreateAsync(GoalValidatorTests.Valid() with { EndDate = new DateOnly(2026, 11, 2) });
        await _timers.TryStartAsync(new ActiveTimer(d.Id, _time.GetUtcNow()));
        _time.Advance(TimeSpan.FromDays(1));
        await Service.ArchiveExpiredAsync();
        (await _timers.GetAsync()).Should().BeNull();
    }

    // Revisão final #2: minutos do cronômetro não podem virar unidades de outra medida.
    [Fact]
    public async Task Measure_change_is_refused_while_goal_has_a_timer()
    {
        var goal = await Service.CreateAsync(GoalValidatorTests.Valid() with { Measure = Measure.Duration, Unit = null, Target = 60 });
        await _timers.TryStartAsync(new ActiveTimer(goal.Id, _time.GetUtcNow()));

        var act = () => Service.UpdateAsync(goal.Id, GoalValidatorTests.Valid());

        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle(e => e.Field == "Measure" && e.Message == "Pare o cronômetro antes de mudar a medida.");
        (await Service.UpdateAsync(goal.Id, GoalValidatorTests.Valid() with { Measure = Measure.Duration, Unit = null, Target = 90 }))
            .Target.Should().Be(90);
    }
}
