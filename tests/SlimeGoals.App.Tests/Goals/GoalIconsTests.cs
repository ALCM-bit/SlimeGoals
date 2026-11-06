using System.Globalization;
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

    [Fact]
    public void Every_icon_stays_inside_the_24_grid()
    {
        foreach (var icon in GoalIcons.All)
        {
            foreach (var (x, y) in EndPoints(icon.PathData))
            {
                x.Should().BeInRange(0, 24, because: $"o ícone {icon.Key} é desenhado numa grade 24×24");
                y.Should().BeInRange(0, 24, because: $"o ícone {icon.Key} é desenhado numa grade 24×24");
            }
        }
    }

    /// <summary>Percorre o path data e devolve o ponto final de cada comando, em coordenadas absolutas.</summary>
    private static IEnumerable<(double X, double Y)> EndPoints(string pathData)
    {
        var tokens = pathData.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var (x, y, startX, startY) = (0.0, 0.0, 0.0, 0.0);
        var index = 0;
        var command = ' ';
        while (index < tokens.Length)
        {
            if (char.IsLetter(tokens[index][0]))
                command = tokens[index++][0];
            if (command is 'Z' or 'z')
            {
                (x, y) = (startX, startY);
                continue;
            }

            var count = char.ToUpperInvariant(command) switch
            {
                'H' or 'V' => 1,
                'M' or 'L' => 2,
                'S' or 'Q' => 4,
                'C' => 6,
                'A' => 7,
                _ => throw new InvalidOperationException($"Comando desconhecido: {command}"),
            };
            var args = tokens[index..(index + count)]
                .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            index += count;

            var relative = char.IsLower(command);
            switch (char.ToUpperInvariant(command))
            {
                case 'H':
                    x = args[0] + (relative ? x : 0);
                    break;
                case 'V':
                    y = args[0] + (relative ? y : 0);
                    break;
                default:
                    (x, y) = (args[^2] + (relative ? x : 0), args[^1] + (relative ? y : 0));
                    break;
            }

            if (char.ToUpperInvariant(command) == 'M')
                (startX, startY) = (x, y);
            yield return (x, y);
        }
    }
}
