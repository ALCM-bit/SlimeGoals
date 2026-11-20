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
            Measure = measure,
            Recurrence = recurrence,
            ActiveDays = days,
            Target = target,
            Unit = measure == Measure.Quantity ? "copos" : null,
        });

    private async Task Seed(Goal goal, int amount) =>
        await _checkins.AddAsync(new Checkin
        {
            Id = Guid.CreateVersion7(T0), GoalId = goal.Id, DoneAt = T0, Amount = amount, UpdatedAt = T0,
        });

    [Fact]
    public async Task Unmark_with_total_2_goes_back_to_zero_in_one_step()
    {
        var goal = await Create(Measure.Completion, target: 1);
        await Seed(goal, 1);
        await Seed(goal, 1);

        (await Service.AdjustAsync(goal.Id, -1)).Should().Be(0);

        _checkins.All.Select(c => c.Amount).Should().Equal(1, 1, -2);
    }

    [Fact]
    public async Task Mark_with_negative_total_reaches_one()
    {
        var goal = await Create(Measure.Completion, target: 1);
        await Seed(goal, -1);

        (await Service.AdjustAsync(goal.Id, 1)).Should().Be(1);

        _checkins.All.Select(c => c.Amount).Should().Equal(-1, 2);
    }

    [Fact]
    public async Task Quantity_with_negative_total_counts_from_zero()
    {
        var goal = await Create();
        await Seed(goal, -3);

        (await Service.AdjustAsync(goal.Id, 2)).Should().Be(2);

        _checkins.All.Select(c => c.Amount).Should().Equal(-3, 5);
    }

    [Fact]
    public async Task Removing_from_zero_limited_total_is_refused()
    {
        var goal = await Create();
        await Seed(goal, -1);

        var act = () => Service.AdjustAsync(goal.Id, -1);

        await act.Should().ThrowAsync<GoalStateException>();
    }

    [Fact]
    public async Task Marking_again_with_total_2_is_refused()
    {
        var goal = await Create(Measure.Completion, target: 1);
        await Seed(goal, 2);

        var act = () => Service.AdjustAsync(goal.Id, 1);

        await act.Should().ThrowAsync<GoalStateException>();
    }

    [Fact]
    public async Task Normal_total_records_the_amount_as_is()
    {
        var goal = await Create();
        await Seed(goal, 3);

        await Service.AdjustAsync(goal.Id, 2);

        _checkins.All.Select(c => c.Amount).Should().Equal(3, 2);
    }

    [Fact]
    public async Task Period_total_is_limited()
    {
        var goal = await Create();
        await Seed(goal, -1);

        (await Service.PeriodTotalAsync(goal.Id)).Should().Be(0);
    }

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
    [InlineData(Measure.Duration, 86_401)]
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

    [Fact]
    public async Task Compensated_adjust_marks_period_and_seen()
    {
        var goal = await Create();
        await Seed(goal, -3);

        (await Service.AdjustAsync(goal.Id, 2)).Should().Be(2);

        var first = _checkins.All[^1];
        first.Amount.Should().Be(5);
        first.Compensation!.Period.Should().Be(new DateOnly(2026, 11, 2));
        first.Compensation.Seen.Should().BeEmpty();

        await Seed(goal, -5); // outro aparelho zera de novo o total real
        await Service.AdjustAsync(goal.Id, 1);

        _checkins.All[^1].Compensation!.Seen.Should().Equal(first.Id);
    }

    [Fact]
    public async Task Normal_adjust_has_no_compensation()
    {
        var goal = await Create();

        await Service.AdjustAsync(goal.Id, 3);

        _checkins.All.Single().Compensation.Should().BeNull();
    }

    [Fact]
    public async Task Recent_notes_are_distinct_newest_first_and_limited_to_5()
    {
        var goal = await Create();
        foreach (var note in new[] { "a", "b", "c", null, "d", "b", "e", "f" })
        {
            await Service.AdjustAsync(goal.Id, 1, note);
            _time.Advance(TimeSpan.FromMinutes(1));
        }

        (await Service.RecentNotesAsync(goal.Id)).Should().Equal("f", "e", "b", "d", "c");
    }

    // ---- Registros de hoje: ver, ajustar e apagar (specs/Tarefas_no_cronometro_v1.md §7.2) ----

    private async Task<Checkin> Record(Goal goal, int amount, string? note = null, DateTimeOffset? at = null,
        Compensation? compensation = null)
    {
        var checkin = new Checkin
        {
            Id = Guid.CreateVersion7(), GoalId = goal.Id, DoneAt = at ?? T0, Amount = amount, Note = note, UpdatedAt = T0,
            Compensation = compensation,
        };
        await _checkins.AddAsync(checkin);
        return checkin;
    }

    [Fact]
    public async Task Duration_accepts_up_to_24_hours_in_seconds()
    {
        var goal = await Create(Measure.Duration, target: 3600);

        (await Service.AdjustAsync(goal.Id, 86_400)).Should().Be(86_400);
        var act = () => Service.AdjustAsync(goal.Id, 86_401);

        var error = (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle().Which;
        error.Message.Should().Be("Valor fora do limite.");
    }

    [Fact]
    public async Task Replace_accepts_seconds()
    {
        var goal = await Timed();
        var old = await Record(goal, 120);

        await Service.ReplaceAsync(goal.Id, old.Id, 160, null);
        Live.Should().ContainSingle().Which.Amount.Should().Be(160);

        var act = () => Service.ReplaceAsync(goal.Id, Live.Single().Id, 0, null);
        (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Informe de 1 s a 24 h.");
    }

    private Task<Goal> Timed() => Create(Measure.Duration, target: 60);

    private IEnumerable<Checkin> Live => _checkins.All.Where(c => c.DeletedAt is null);

    [Fact]
    public async Task Today_lists_todays_checkins_newest_first()
    {
        var goal = await Timed();
        var early = await Record(goal, 10, at: T0.AddHours(-2)); // 07:00 local
        var later = await Record(goal, 15, at: T0.AddMinutes(-30)); // 08:30 local
        await Record(goal, 20, at: T0.AddDays(-1));

        (await Service.TodayAsync()).Select(c => c.Id).Should().Equal(later.Id, early.Id);
    }

    [Fact]
    public async Task Replace_changes_minutes_and_name_keeping_the_time()
    {
        var goal = await Timed();
        var old = await Record(goal, 25, "Inglês", T0.AddMinutes(-40));

        (await Service.ReplaceAsync(goal.Id, old.Id, 20, " Leitura ")).Should().Be(20);

        var now = Live.Should().ContainSingle().Which;
        (now.Id == old.Id).Should().BeFalse();
        (now.DoneAt, now.Amount, now.Note, now.UpdatedAt, now.Compensation).Should().Be((old.DoneAt, 20, "Leitura", T0, null));
    }

    [Fact]
    public async Task Replace_keeps_a_negative_record_negative()
    {
        var goal = await Timed();
        await Record(goal, 30);
        var removed = await Record(goal, -5);

        (await Service.ReplaceAsync(goal.Id, removed.Id, 10, null)).Should().Be(20);
    }

    [Fact]
    public async Task Delete_removes_the_record_from_the_total()
    {
        var goal = await Timed();
        await Record(goal, 25);
        var chunk = await Record(goal, 12);

        (await Service.DeleteAsync(goal.Id, chunk.Id)).Should().Be(25);
    }

    [Fact]
    public async Task Delete_of_a_negative_record_raises_the_total()
    {
        var goal = await Timed();
        await Record(goal, 30);
        var removed = await Record(goal, -5);

        (await Service.DeleteAsync(goal.Id, removed.Id)).Should().Be(30);
    }

    [Fact]
    public async Task Change_that_would_leave_the_total_negative_is_refused()
    {
        var goal = await Timed();
        var added = await Record(goal, 10);
        await Record(goal, -10);

        var delete = () => Service.DeleteAsync(goal.Id, added.Id);
        await delete.Should().ThrowAsync<GoalStateException>().WithMessage("O total do período ficaria negativo.");
        var shrink = () => Service.ReplaceAsync(goal.Id, added.Id, 5, null);
        await shrink.Should().ThrowAsync<GoalStateException>().WithMessage("O total do período ficaria negativo.");
        Live.Should().HaveCount(2);
    }

    [Fact]
    public async Task Change_that_raises_a_negative_raw_total_is_allowed()
    {
        var goal = await Timed();
        await Record(goal, 10);
        var removed = await Record(goal, -30);

        (await Service.DeleteAsync(goal.Id, removed.Id)).Should().Be(10);
    }

    [Fact]
    public async Task Yesterday_checkin_cannot_be_changed()
    {
        var goal = await Timed();
        var old = await Record(goal, 25, at: T0.AddDays(-1));

        var act = () => Service.ReplaceAsync(goal.Id, old.Id, 20, null);
        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Só dá para ajustar registros de hoje.");
    }

    [Fact]
    public async Task Compensation_cannot_be_changed()
    {
        var goal = await Timed();
        var merged = await Record(goal, 5, compensation: new Compensation(new DateOnly(2026, 11, 2), []));
        const string message = "Este registro vem da junção de aparelhos e não pode ser ajustado.";

        var replace = () => Service.ReplaceAsync(goal.Id, merged.Id, 3, null);
        await replace.Should().ThrowAsync<GoalStateException>().WithMessage(message);
        var delete = () => Service.DeleteAsync(goal.Id, merged.Id);
        await delete.Should().ThrowAsync<GoalStateException>().WithMessage(message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(24 * 3600 + 1)]
    public async Task Seconds_out_of_range_are_refused(int seconds)
    {
        var goal = await Timed();
        var old = await Record(goal, 25);

        var act = () => Service.ReplaceAsync(goal.Id, old.Id, seconds, null);
        var error = (await act.Should().ThrowAsync<GoalValidationException>()).Which.Errors.Should().ContainSingle().Which;
        (error.Field, error.Message).Should().Be(("Amount", "Informe de 1 s a 24 h."));
    }

    [Fact]
    public async Task Missing_checkin_reports_that_it_changed()
    {
        var goal = await Timed();
        var other = await Timed();
        var foreign = await Record(other, 10);

        var missing = () => Service.DeleteAsync(goal.Id, Guid.CreateVersion7());
        await missing.Should().ThrowAsync<GoalStateException>().WithMessage("O registro mudou. Confira em Hoje.");
        var wrongGoal = () => Service.ReplaceAsync(goal.Id, foreign.Id, 5, null);
        await wrongGoal.Should().ThrowAsync<GoalStateException>().WithMessage("O registro mudou. Confira em Hoje.");
    }

    [Fact]
    public async Task Replace_only_for_duration_goals()
    {
        var goal = await Create(Measure.Quantity);
        var old = await Record(goal, 2);

        var act = () => Service.ReplaceAsync(goal.Id, old.Id, 1, null);
        await act.Should().ThrowAsync<GoalStateException>().WithMessage("Só dá para ajustar registros de metas de tempo.");
    }

    [Fact]
    public async Task Unchanged_replace_writes_nothing()
    {
        var goal = await Timed();
        var old = await Record(goal, 25, "Inglês");

        (await Service.ReplaceAsync(goal.Id, old.Id, 25, "Inglês ")).Should().Be(25);

        _checkins.All.Should().ContainSingle().Which.Id.Should().Be(old.Id);
    }

    [Fact]
    public async Task Replace_and_delete_notify_changes()
    {
        var goal = await Timed();
        var first = await Record(goal, 25);
        var second = await Record(goal, 12);
        var changes = new DataChangeNotifier();
        var count = 0;
        changes.Changed += (_, _) => count++;
        var service = new CheckinService(_goals, _checkins, _time, changes);

        await service.ReplaceAsync(goal.Id, first.Id, 20, null);
        await service.DeleteAsync(goal.Id, second.Id);

        count.Should().Be(2);
    }
}
