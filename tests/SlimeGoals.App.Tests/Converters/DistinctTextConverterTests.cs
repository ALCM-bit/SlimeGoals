using System.Globalization;
using SlimeGoals.App.Converters;

namespace SlimeGoals.App.Tests.Converters;

/// <summary>O erro do Pomodoro só aparece quando não repete o motivo de "Começar" estar desligado.</summary>
public class DistinctTextConverterTests
{
    private static object Convert(params object?[] values) =>
        DistinctTextConverter.Instance.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Shows_a_text_that_differs_from_the_others() =>
        Convert("Falhou ao salvar.", "Pare o cronômetro antes.").Should().Be(true);

    [Fact]
    public void Hides_a_text_equal_to_another() =>
        Convert("Pare o cronômetro antes.", "Pare o cronômetro antes.").Should().Be(false);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hides_a_missing_text(string? first) => Convert(first, null).Should().Be(false);
}
