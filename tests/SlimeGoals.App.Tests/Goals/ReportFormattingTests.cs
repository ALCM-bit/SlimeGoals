using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App.Tests.Goals;

public class ReportFormattingTests
{
    private static Goal NewGoal(Recurrence recurrence = Recurrence.Daily, Measure measure = Measure.Quantity,
        int target = 8, string? unit = "copos") => new()
    {
        Id = Guid.NewGuid(), Title = "Meta", Recurrence = recurrence, Target = target, Measure = measure, Unit = unit,
        Icon = "water", Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new TimeOnly(8, 0),
        ActiveUntil = new TimeOnly(20, 0), ActiveDays = WeekDays.All, CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static WeekGoalRow Week(Goal goal, int done = 0, int met = 0, int counted = 0, DateOnly? best = null) =>
        new(goal, done, met, counted, null, best);

    private static MonthGoalRow Month(Goal goal, int met, int counted, Streak streak) =>
        new(goal, met, counted, null, [], streak);

    [Theory]
    [InlineData(2026, 11, 2, "2 a 8 nov 2026")]
    [InlineData(2026, 10, 26, "26 out a 1 nov 2026")]
    [InlineData(2025, 12, 29, "29 dez 2025 a 4 jan 2026")]
    public void WeekTitle_covers_same_month_months_and_years(int y, int m, int d, string expected)
    {
        var start = new DateOnly(y, m, d);
        ReportFormatting.WeekTitle(new DateRange(start, start.AddDays(7))).Should().Be(expected);
    }

    [Fact]
    public void Day_entry_shows_day_value_against_target_for_daily_and_contribution_for_weekly()
    {
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(), 8, true)).Should().Be("8 de 8 copos");
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(measure: Measure.Duration, target: 30, unit: null), 40, true))
            .Should().Be("40 min de 30 min");
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(measure: Measure.Completion, target: 1, unit: null), 1, true))
            .Should().Be("Feito");
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(Recurrence.Weekly, Measure.Duration, 300, null), 45, null))
            .Should().Be("45 min");
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(Recurrence.Weekly, target: 20), 1, null))
            .Should().Be("1 copos");
        ReportFormatting.DayEntry(new DayGoalEntry(NewGoal(Recurrence.Weekly, target: 20, unit: "vezes"), 1, null))
            .Should().Be("1 vez");
    }

    [Fact]
    public void Detail_titles()
    {
        ReportFormatting.DayTitle(new DateOnly(2026, 11, 2)).Should().Be("Segunda, 2 nov");
        ReportFormatting.DayTitle(new DateOnly(2026, 11, 8)).Should().Be("Domingo, 8 nov");
        ReportFormatting.WeekOf(new DateOnly(2026, 10, 26)).Should().Be("Semana de 26 out");
    }

    [Fact]
    public void Month_texts()
    {
        ReportFormatting.MonthTitle(2026, 11).Should().Be("Novembro de 2026");
        ReportFormatting.MonthSubtitle(ReportService.MonthRange(2026, 11)).Should().Be("Semanas de 2 nov a 6 dez");
    }

    [Fact]
    public void Percent_and_count()
    {
        ReportFormatting.Percent(80).Should().Be("80%");
        ReportFormatting.Percent(null).Should().Be("—");
        ReportFormatting.Count(1, "dia", "dias").Should().Be("1 dia");
        ReportFormatting.Count(2, "dia", "dias").Should().Be("2 dias");
        ReportFormatting.Count(0, "dia", "dias").Should().Be("0 dias");
    }

    [Fact]
    public void WeekDetail_per_goal_kind()
    {
        ReportFormatting.WeekDetail(Week(NewGoal(), met: 4, counted: 6)).Should().Be("4 de 6 dias");
        ReportFormatting.WeekDetail(Week(NewGoal(), met: 1, counted: 1)).Should().Be("1 de 1 dia");
        ReportFormatting.WeekDetail(Week(NewGoal(Recurrence.Weekly, target: 8), done: 6)).Should().Be("6 de 8 copos");
        ReportFormatting.WeekDetail(Week(NewGoal(Recurrence.Weekly, Measure.Duration, 300, null), done: 240))
            .Should().Be("4 h de 5 h");
        var completion = NewGoal(Recurrence.Weekly, Measure.Completion, 1, null);
        ReportFormatting.WeekDetail(Week(completion, done: 1)).Should().Be("Feito");
        ReportFormatting.WeekDetail(Week(completion, done: 0)).Should().Be("Pendente");
    }

    [Fact]
    public void MonthDetail_and_streak()
    {
        ReportFormatting.MonthDetail(Month(NewGoal(), 18, 25, default)).Should().Be("18 de 25 dias");
        var weekly = NewGoal(Recurrence.Weekly);
        ReportFormatting.MonthDetail(Month(weekly, 3, 4, default)).Should().Be("3 de 4 semanas");
        ReportFormatting.MonthDetail(Month(weekly, 1, 1, default)).Should().Be("1 de 1 semana");
        ReportFormatting.Streak(Month(NewGoal(), 0, 0, new Streak(5, 12))).Should().Be("Sequência: 5 dias · maior: 12 dias");
        ReportFormatting.Streak(Month(weekly, 0, 0, new Streak(1, 2))).Should().Be("Sequência: 1 semana · maior: 2 semanas");
    }

    [Fact]
    public void BestDay_and_reminders()
    {
        ReportFormatting.BestDay(Week(NewGoal(), best: new DateOnly(2026, 11, 4))).Should().Be("Melhor dia: qua");
        ReportFormatting.BestDay(Week(NewGoal(), best: new DateOnly(2026, 11, 8))).Should().Be("Melhor dia: dom");
        ReportFormatting.BestDay(Week(NewGoal())).Should().BeNull();
        ReportFormatting.Reminders(12, 7).Should().Be("12 disparados · 7 respondidos");
        ReportFormatting.Reminders(1, 0).Should().Be("1 disparado · 0 respondidos");
    }
}
