using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Tests.Goals;

public class GoalFormattingTests
{
    [Theory]
    [InlineData(Recurrence.Daily, 1, "1× por dia")]
    [InlineData(Recurrence.Weekly, 5, "5× por semana")]
    public void Target(Recurrence r, int target, string expected) =>
        GoalFormatting.Target(r, target).Should().Be(expected);

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
        GoalFormatting.Summary(Sample()).Should().Be("5× por semana · a cada 2 h · 09:00–18:00 · Seg a sex");
        GoalFormatting.Summary(Sample(end: new(2026, 11, 15)))
            .Should().Be("5× por semana · a cada 2 h · 09:00–18:00 · Seg a sex · Até 15/11");
    }
}
