using SlimeGoals.App.Account;

namespace SlimeGoals.App.Tests.Account;

public class ServerAddressTests
{
    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("https://sync.exemplo.com/")]
    [InlineData("  http://127.0.0.1:8080  ")]
    public void Accepts_absolute_http_and_https(string text)
    {
        ServerAddress.TryParse(text, out var server, out var error).Should().BeTrue();

        server.Should().Be(new Uri(text.Trim()));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://x")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("/home/ana")]
    [InlineData("http://x/?a=1")]
    [InlineData("http://x/?")]
    [InlineData("http://x/#y")]
    [InlineData("http://u:p@host")]
    [InlineData("http://u@host")]
    public void Rejects_everything_else(string text)
    {
        ServerAddress.TryParse(text, out var server, out var error).Should().BeFalse();

        server.Should().BeNull();
        error.Should().Be("Informe um endereço http:// ou https:// completo.");
    }
}
