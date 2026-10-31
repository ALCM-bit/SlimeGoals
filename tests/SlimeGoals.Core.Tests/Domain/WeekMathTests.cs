using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class WeekMathTests
{
    [Theory] // 2026-11-02 é segunda
    [InlineData("2026-11-02", "2026-11-02")]
    [InlineData("2026-11-08", "2026-11-02")] // domingo pertence à semana anterior
    [InlineData("2026-11-05", "2026-11-02")]
    public void StartOfWeek_is_monday(string date, string monday) =>
        WeekMath.StartOfWeek(DateOnly.Parse(date)).Should().Be(DateOnly.Parse(monday));
}
