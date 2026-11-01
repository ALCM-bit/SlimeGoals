using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Tests.Goals;

public class GoalFormattingTests
{
    [Theory]
    [InlineData(Measure.Completion, Recurrence.Daily, 1, null, "Concluir no dia")]
    [InlineData(Measure.Completion, Recurrence.Weekly, 1, null, "Concluir na semana")]
    [InlineData(Measure.Quantity, Recurrence.Daily, 8, "copos", "8 copos por dia")]
    [InlineData(Measure.Quantity, Recurrence.Weekly, 3, "vezes", "3 vezes por semana")]
    [InlineData(Measure.Duration, Recurrence.Daily, 45, null, "45 min por dia")]
    [InlineData(Measure.Duration, Recurrence.Weekly, 300, null, "5 h por semana")]
    public void Target(Measure m, Recurrence r, int target, string? unit, string expected) =>
        GoalFormatting.Target(Sample() with { Measure = m, Recurrence = r, Target = target, Unit = unit }).Should().Be(expected);

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(45, "45 min")]
    [InlineData(120, "2 h")]
    [InlineData(150, "2 h 30")]
    [InlineData(-30, "-30 min")]
    public void Duration(int minutes, string expected) => GoalFormatting.Duration(minutes).Should().Be(expected);

    [Theory]
    [InlineData(309, "0:05:09")]
    [InlineData(3723, "1:02:03")]
    [InlineData(36000, "10:00:00")]
    public void Elapsed(int seconds, string expected) =>
        GoalFormatting.Elapsed(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Theory]
    [InlineData(Measure.Completion, Recurrence.Daily, 1, null, 1, "Feito hoje")]
    [InlineData(Measure.Completion, Recurrence.Weekly, 1, null, 1, "Feito na semana")]
    [InlineData(Measure.Completion, Recurrence.Daily, 1, null, 0, "Pendente")]
    [InlineData(Measure.Quantity, Recurrence.Daily, 8, "copos", 5, "5 de 8 copos hoje")]
    [InlineData(Measure.Quantity, Recurrence.Weekly, 3, "vezes", 2, "2 de 3 vezes na semana")]
    [InlineData(Measure.Duration, Recurrence.Weekly, 300, null, 150, "2 h 30 de 5 h na semana")]
    [InlineData(Measure.Duration, Recurrence.Daily, 45, null, 0, "0 min de 45 min hoje")]
    public void Progress(Measure m, Recurrence r, int target, string? unit, int done, string expected)
    {
        var goal = Sample() with { Measure = m, Recurrence = r, Target = target, Unit = unit };
        GoalFormatting.Progress(new GoalProgress(goal, done, done >= target)).Should().Be(expected);
    }

    [Theory]
    [InlineData(30, "30 min")]
    [InlineData(60, "1 h")]
    [InlineData(90, "1 h 30 min")]
    [InlineData(1440, "24 h")]
    public void Interval(int minutes, string expected) => GoalFormatting.Interval(minutes).Should().Be(expected);

    [Fact]
    public void Window() => GoalFormatting.Window(new(9, 0), new(18, 30)).Should().Be("09:00–18:30");

    [Theory]
    [InlineData(WeekDays.All, "Todos os dias")]
    [InlineData(WeekDays.Weekdays, "Seg a sex")]
    [InlineData(WeekDays.Saturday | WeekDays.Sunday, "Fim de semana")]
    [InlineData(WeekDays.Monday | WeekDays.Wednesday | WeekDays.Friday, "Seg, Qua, Sex")]
    [InlineData(WeekDays.Sunday, "Dom")]
    public void Days(WeekDays days, string expected) => GoalFormatting.Days(days).Should().Be(expected);

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("2026-11-05", null, "De 05/11")]
    [InlineData(null, "2026-11-15", "Até 15/11")]
    [InlineData("2026-11-05", "2026-11-15", "05/11 a 15/11")]
    public void Period(string? start, string? end, string expected) =>
        GoalFormatting.Period(start is null ? null : DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end))
            .Should().Be(expected);

    private static Goal Sample(DateOnly? start = null, DateOnly? end = null) => new()
    {
        Id = Guid.NewGuid(), Title = "Estudar", Recurrence = Recurrence.Weekly, Target = 5, Icon = "book",
        Color = GoalColor.Purple, ReminderEveryMinutes = 120, ActiveFrom = new(9, 0), ActiveUntil = new(18, 0),
        ActiveDays = WeekDays.Weekdays, StartDate = start, EndDate = end,
        CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Summary_includes_period_only_when_set()
    {
        GoalFormatting.Summary(Sample()).Should().Be("5 vezes por semana · a cada 2 h · 09:00–18:00 · Seg a sex");
        GoalFormatting.Summary(Sample(end: new(2026, 11, 15)))
            .Should().Be("5 vezes por semana · a cada 2 h · 09:00–18:00 · Seg a sex · Até 15/11");
    }

    [Fact]
    public void Summary_uses_measure_target() =>
        GoalFormatting.Summary(Sample() with { Measure = Measure.Duration, Target = 150, Unit = null })
            .Should().StartWith("2 h 30 por semana · ");
}
