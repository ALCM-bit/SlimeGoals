using SlimeGoals.Core.Domain;

namespace SlimeGoals.Core.Tests.Domain;

public class WeekDaysTests
{
    [Theory]
    [InlineData(WeekDays.Weekdays, "1,2,3,4,5")]
    [InlineData(WeekDays.Sunday | WeekDays.Monday, "1,7")]
    [InlineData(WeekDays.All, "1,2,3,4,5,6,7")]
    public void Storage_round_trips(WeekDays days, string stored)
    {
        days.ToStorage().Should().Be(stored);
        WeekDaysExtensions.ParseStorage(stored).Should().Be(days);
    }

    [Fact]
    public void Includes_maps_DayOfWeek_sunday_to_flag_sunday()
    {
        WeekDays.Sunday.Includes(DayOfWeek.Sunday).Should().BeTrue();
        WeekDays.Sunday.Includes(DayOfWeek.Monday).Should().BeFalse();
        WeekDays.Weekdays.Includes(DayOfWeek.Friday).Should().BeTrue();
        WeekDays.Weekdays.Includes(DayOfWeek.Saturday).Should().BeFalse();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("8")]
    [InlineData("1,x")]
    public void ParseStorage_rejects_invalid_tokens(string s) =>
        FluentActions.Invoking(() => WeekDaysExtensions.ParseStorage(s)).Should().Throw<FormatException>();
}
