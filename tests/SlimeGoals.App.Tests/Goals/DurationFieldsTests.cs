using SlimeGoals.App.Goals;

namespace SlimeGoals.App.Tests.Goals;

public class DurationFieldsTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(160, 0, 2, 40)]
    [InlineData(4360, 1, 12, 40)]
    [InlineData(-160, 0, 2, 40)] // sem sinal: o sinal do registro fica com quem chama
    [InlineData(int.MinValue, 596523, 14, 8)] // o valor absoluto não cabe em int
    public void Split_gives_hours_minutes_and_seconds_without_sign(int seconds, int hours, int minutes, int rest) =>
        DurationFields.Split(seconds).Should().Be(((decimal)hours, (decimal)minutes, (decimal)rest));

    [Fact]
    public void Seconds_is_null_only_when_all_fields_are_empty()
    {
        DurationFields.Seconds(null, null, null).Should().BeNull();
        DurationFields.Seconds(null, null, 45).Should().Be(45);
        DurationFields.Seconds(null, 0, null).Should().Be(0);
        DurationFields.Seconds(1, 12, 40).Should().Be(4360);
    }

    [Theory]
    [InlineData(0, 2, 40.5, 161)]
    [InlineData(0, 2, 40.4, 160)]
    [InlineData(0, 0.5, 0, 30)]
    public void Seconds_rounds_fractional_input_away_from_zero(double hours, double minutes, double seconds, int expected) =>
        DurationFields.Seconds((decimal)hours, (decimal)minutes, (decimal)seconds).Should().Be(expected);

    [Fact]
    public void Seconds_clamps_very_large_values_instead_of_overflowing()
    {
        DurationFields.Seconds(decimal.MaxValue, decimal.MaxValue, decimal.MaxValue).Should().Be(1_000_000_000);
        DurationFields.Seconds(decimal.MinValue, null, null).Should().Be(-1_000_000_000);
        DurationFields.Seconds(1_000_000, null, null).Should().Be(1_000_000_000);
    }
}
