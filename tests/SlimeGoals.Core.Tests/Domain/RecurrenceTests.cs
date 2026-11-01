using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class RecurrenceTests
{
    // Tarefa única removida em 01/11/2026 (spec §15): só hábitos diários e metas semanais.
    [Fact]
    public void Only_daily_and_weekly_exist() =>
        Enum.GetValues<Recurrence>().Should().Equal(Recurrence.Daily, Recurrence.Weekly);
}
