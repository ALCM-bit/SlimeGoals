using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Tests.ViewModels;

public class TagTextTests
{
    [Theory]
    [InlineData("saúde", "Saúde")]
    [InlineData("éter", "Éter")]
    [InlineData("organização da casa", "Organização da casa")]
    [InlineData("1º lugar", "1º lugar")]
    [InlineData("", "")]
    public void Display_capitalizes_only_the_first_letter(string name, string expected) =>
        TagText.Display(name).Should().Be(expected);
}
