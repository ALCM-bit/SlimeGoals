using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Validation;

namespace SlimeGoals.Core.Tests.Validation;

public class GoalValidatorTests
{
    internal static GoalDraft Valid() => new(
        Title: "Estudar inglês", Recurrence: Recurrence.Weekly, Target: 5, Icon: "book", Color: GoalColor.Purple,
        ReminderEveryMinutes: 120, ActiveFrom: new TimeOnly(9, 0), ActiveUntil: new TimeOnly(18, 0),
        ActiveDays: WeekDays.Weekdays, StartDate: null, EndDate: null);

    private static void ShouldHaveSingleError(GoalDraft draft, string field, string message) =>
        GoalValidator.Validate(draft).Should().ContainSingle()
            .Which.Should().Be(new ValidationError(field, message));

    [Fact]
    public void Valid_draft_has_no_errors() => GoalValidator.Validate(Valid()).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_title_is_rejected(string title) =>
        ShouldHaveSingleError(Valid() with { Title = title }, nameof(GoalDraft.Title), "Informe um título.");

    [Fact]
    public void Title_length_is_measured_after_trim()
    {
        GoalValidator.Validate(Valid() with { Title = "  " + new string('a', 80) + "  " }).Should().BeEmpty();
        ShouldHaveSingleError(Valid() with { Title = new string('a', 81) },
            nameof(GoalDraft.Title), "O título deve ter até 80 caracteres.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Recurring_target_must_be_1_to_99(int target) =>
        ShouldHaveSingleError(Valid() with { Target = target },
            nameof(GoalDraft.Target), "O alvo deve ser entre 1 e 99.");

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public void Reminder_interval_must_be_5_to_1440(int minutes) =>
        ShouldHaveSingleError(Valid() with { ReminderEveryMinutes = minutes },
            nameof(GoalDraft.ReminderEveryMinutes), "O intervalo deve ser entre 5 minutos e 24 horas.");

    [Theory]
    [InlineData(18, 0)]
    [InlineData(9, 0)]
    public void Active_window_end_must_be_after_start(int h, int m) =>
        ShouldHaveSingleError(Valid() with { ActiveFrom = new TimeOnly(18, 0), ActiveUntil = new TimeOnly(h, m) },
            nameof(GoalDraft.ActiveUntil), "O fim do horário deve ser depois do início.");

    [Fact]
    public void At_least_one_day_is_required() =>
        ShouldHaveSingleError(Valid() with { ActiveDays = WeekDays.None },
            nameof(GoalDraft.ActiveDays), "Escolha ao menos um dia.");

    [Fact]
    public void End_date_cannot_precede_start_date()
    {
        GoalValidator.Validate(Valid() with { StartDate = new(2026, 11, 5), EndDate = new(2026, 11, 5) }).Should().BeEmpty();
        ShouldHaveSingleError(Valid() with { StartDate = new(2026, 11, 5), EndDate = new(2026, 11, 4) },
            nameof(GoalDraft.EndDate), "A data de fim deve ser igual ou posterior à de início.");
    }

    [Fact]
    public void Icon_is_required() =>
        ShouldHaveSingleError(Valid() with { Icon = " " }, nameof(GoalDraft.Icon), "Escolha um ícone.");

    [Fact]
    public void All_errors_are_reported_together() =>
        GoalValidator.Validate(Valid() with { Title = "", ActiveDays = WeekDays.None })
            .Select(e => e.Field).Should().BeEquivalentTo([nameof(GoalDraft.Title), nameof(GoalDraft.ActiveDays)]);
}
