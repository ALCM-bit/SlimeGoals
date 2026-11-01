using SlimeGoals.App.Goals;

namespace SlimeGoals.App.Tests.Goals;

public class GoalIconsTests
{
    [Fact]
    public void All_has_16_unique_keys_in_order() =>
        GoalIcons.All.Select(i => i.Key).Should().Equal(
            "book", "run", "water", "workout", "sleep", "health", "language", "code",
            "music", "art", "money", "nature", "coffee", "medicine", "star", "target");

    [Fact]
    public void Every_icon_has_name_and_path_data()
    {
        foreach (var icon in GoalIcons.All)
        {
            icon.Name.Should().NotBeNullOrWhiteSpace();
            icon.PathData.Should().MatchRegex("^[Mm]", because: $"o ícone {icon.Key} precisa começar com um MoveTo");
        }
    }

    [Theory]
    [InlineData("nao-existe")]
    [InlineData("")]
    [InlineData(null)]
    public void Get_unknown_or_null_key_returns_target(string? key) =>
        GoalIcons.Get(key).Key.Should().Be(GoalIcons.DefaultKey).And.Be("target");

    [Fact]
    public void Get_known_key_returns_that_icon() => GoalIcons.Get("water").Name.Should().Be("Água");
}
