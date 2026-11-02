using SlimeGoals.App.Goals;
using SlimeGoals.Core.Domain;

namespace SlimeGoals.App.Tests.Goals;

public class ReminderFormatterTests
{
    private static Goal Sample(Measure measure, int target, string? unit) => new()
    {
        Id = Guid.NewGuid(), Title = "Meta", Recurrence = Recurrence.Daily, Target = target, Measure = measure, Unit = unit,
        Icon = "water", Color = GoalColor.Green, ReminderEveryMinutes = 60, ActiveFrom = new(8, 0), ActiveUntil = new(20, 0),
        ActiveDays = WeekDays.All, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Theory]
    [InlineData(Measure.Completion, 1, null, 0, "Pendente", "Feito")]
    [InlineData(Measure.Quantity, 8, "copos", 3, "3 de 8 copos hoje", "+1")]
    [InlineData(Measure.Duration, 120, null, 90, "1 h 30 de 2 h hoje", "Iniciar")]
    public void Formatter_uses_progress_text_and_primary_label_per_measure(
        Measure measure, int target, string? unit, int total, string body, string label)
    {
        var goal = Sample(measure, target, unit) with { Title = "Beber água" };

        new ReminderFormatter().Format(goal, total).Should().Be(new ReminderText("Beber água", body, label));
    }
}
