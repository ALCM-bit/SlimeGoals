using Microsoft.Extensions.Time.Testing;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;
using SlimeGoals.Core.Tests.Validation;
using SlimeGoals.Core.Validation;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class CheckinServiceTests
{
    // Segunda, 02/11/2026 09:00 em São Paulo (UTC-3).
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeTimeProvider _time = new(T0);
    private readonly InMemoryGoalRepository _goals = new();
    private readonly InMemoryCheckinRepository _checkins = new();
    private readonly InMemoryTimerRepository _timers = new();

    public CheckinServiceTests() => _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));

    private GoalService Goals => new(_goals, _checkins, _timers, _time);
    private CheckinService Service => new(_goals, _checkins, _time);

    private Task<Goal> Create(Measure measure = Measure.Quantity, Recurrence recurrence = Recurrence.Daily,
        WeekDays days = WeekDays.All, int target = 8) =>
        Goals.CreateAsync(GoalValidatorTests.Valid() with
        {
            Measure = measure, Recurrence = recurrence, ActiveDays = days, Target = target,
            Unit = measure == Measure.Quantity ? "copos" : null,
        });

    [Fact]
    public async Task Adjust_adds_and_returns_period_total()
    {
        var goal = await Create();

        (await Service.AdjustAsync(goal.Id, 3, "manhã")).Should().Be(3);
        (await Service.AdjustAsync(goal.Id, -1)).Should().Be(2);

        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(2);
        _checkins.All.Select(c => (c.Amount, c.Note)).Should().Equal((3, "manhã"), (-1, (string?)null));
        _checkins.All.Should().OnlyContain(c => c.DoneAt == T0 && c.UpdatedAt == T0 && c.GoalId == goal.Id);
    }

    [Fact]
    public async Task Removing_more_than_total_is_refused_and_nothing_is_saved()
    {
        var goal = await Create(Measure.Duration, target: 60);
        await Service.AdjustAsync(goal.Id, 30);

        var act = () => Service.AdjustAsync(goal.Id, -40);

        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Não dá para remover mais do que o registrado no período.");
        _checkins.All.Should().ContainSingle();
        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(30);
    }

    [Fact]
    public async Task Completion_accepts_1_then_refuses_second_1_and_accepts_minus_1()
    {
        var goal = await Create(Measure.Completion, target: 1);

        (await Service.AdjustAsync(goal.Id, 1)).Should().Be(1);
        var again = () => Service.AdjustAsync(goal.Id, 1);
        await again.Should().ThrowAsync<GoalStateException>().WithMessage("Meta já concluída neste período.");
        (await Service.AdjustAsync(goal.Id, -1)).Should().Be(0);
        var undoAgain = () => Service.AdjustAsync(goal.Id, -1);
        await undoAgain.Should().ThrowAsync<GoalStateException>();
    }

    [Theory]
    [InlineData(Measure.Quantity, 0)]
    [InlineData(Measure.Quantity, 10000)]
    [InlineData(Measure.Quantity, -10000)]
    [InlineData(Measure.Duration, 1441)]
    [InlineData(Measure.Completion, 2)]
    public async Task Zero_and_out_of_limit_amounts_are_rejected(Measure measure, int amount)
    {
        var goal = await Create(measure, target: measure == Measure.Completion ? 1 : 60);

        var act = () => Service.AdjustAsync(goal.Id, amount);

        var error = (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be("Amount");
        error.Message.Should().Be(amount == 0 ? "Informe um valor diferente de zero." : "Valor fora do limite.");
    }

    [Fact]
    public async Task Note_is_trimmed_empty_becomes_null_and_201_chars_rejected()
    {
        var goal = await Create();

        await Service.AdjustAsync(goal.Id, 1, "  ok  ");
        await Service.AdjustAsync(goal.Id, 1, "   ");
        var act = () => Service.AdjustAsync(goal.Id, 1, new string('a', 201));

        _checkins.All.Select(c => c.Note).Should().Equal("ok", null);
        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should()
            .ContainSingle(e => e.Field == "Note" && e.Message == "A nota deve ter até 200 caracteres.");
    }

    [Fact]
    public async Task Archived_or_inactive_today_goal_is_refused()
    {
        var archived = await Create();
        await Goals.ArchiveAsync(archived.Id);
        var weekend = await Create(days: WeekDays.Saturday | WeekDays.Sunday);

        var onArchived = () => Service.AdjustAsync(archived.Id, 1);
        var onInactive = () => Service.AdjustAsync(weekend.Id, 1);
        var onMissing = () => Service.AdjustAsync(Guid.NewGuid(), 1);

        await onArchived.Should().ThrowAsync<GoalStateException>().WithMessage("Meta arquivada não recebe registros.");
        await onInactive.Should().ThrowAsync<GoalStateException>().WithMessage("Esta meta não está ativa hoje.");
        await onMissing.Should().ThrowAsync<GoalNotFoundException>();
    }

    [Fact]
    public async Task Late_night_local_checkin_counts_for_local_day()
    {
        var goal = await Create();
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 2, 30, 0, TimeSpan.Zero)); // 02/11 23:30 local

        await Service.AdjustAsync(goal.Id, 2);
        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(2);

        _time.SetUtcNow(new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero)); // 03/11 00:30 local
        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Weekly_total_resets_on_local_monday()
    {
        var goal = await Create(recurrence: Recurrence.Weekly);
        _time.SetUtcNow(new DateTimeOffset(2026, 11, 9, 2, 0, 0, TimeSpan.Zero)); // domingo 08/11 23:00 local
        await Service.AdjustAsync(goal.Id, 4);
        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(4);

        _time.SetUtcNow(new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero)); // segunda 09/11 00:00 local
        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(0);
    }
}
