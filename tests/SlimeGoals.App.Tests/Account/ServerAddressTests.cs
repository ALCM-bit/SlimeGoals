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
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("http://[::1]:8080")]
    public void Http_on_loopback_is_accepted(string text)
    {
        ServerAddress.TryParse(text, out var server, out var error).Should().BeTrue();

        server.Should().Be(new Uri(text));
        error.Should().BeNull();
    }

    [Theory]
    [InlineData("http://exemplo.com")]
    [InlineData("http://192.168.0.10:8080")]
    [InlineData("http://sync.exemplo.com:8080/")]
    public void Http_elsewhere_requires_https(string text)
    {
        ServerAddress.TryParse(text, out var server, out var error).Should().BeFalse();

        server.Should().BeNull();
        error.Should().Be("Use https://. O http:// só vale para 127.0.0.1.");
    }

    [Theory]
    [InlineData("https://exemplo.com")]
    [InlineData("https://192.168.0.10:8443/")]
    [InlineData("https://127.0.0.1:8443")]
    public void Https_anywhere_is_accepted(string text)
    {
        ServerAddress.TryParse(text, out var server, out var error).Should().BeTrue();

        server.Should().Be(new Uri(text));
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
