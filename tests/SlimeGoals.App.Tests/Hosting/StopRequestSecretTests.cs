using SlimeGoals.App.Hosting;

namespace SlimeGoals.App.Tests.Hosting;

/// <summary>
/// O código que só o "Parar" da notificação conhece: a tela principal do Android é exportada, e sem ele qualquer
/// app poderia parar o cronômetro.
/// </summary>
public class StopRequestSecretTests
{
    [Fact]
    public void Each_new_secret_is_different() =>
        StopRequestSecret.Create().Should().NotBe(StopRequestSecret.Create());

    [Fact]
    public void A_secret_has_256_bits_in_hex() =>
        StopRequestSecret.Create().Should().MatchRegex("^[0-9A-F]{64}$");

    [Fact]
    public void The_same_secret_matches()
    {
        var secret = StopRequestSecret.Create();

        StopRequestSecret.Matches(secret, new string(secret)).Should().BeTrue();
    }

    [Fact]
    public void Another_secret_does_not_match() =>
        StopRequestSecret.Matches(StopRequestSecret.Create(), StopRequestSecret.Create()).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("true")]
    public void A_request_without_the_secret_does_not_match(string? received) =>
        StopRequestSecret.Matches(StopRequestSecret.Create(), received).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Without_a_stored_secret_nothing_matches(string? expected) =>
        StopRequestSecret.Matches(expected, expected).Should().BeFalse();
}
