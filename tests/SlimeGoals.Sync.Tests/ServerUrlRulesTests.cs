namespace SlimeGoals.Sync.Tests;

/// <summary>A regra do endereço do servidor, a mesma na tela e no coordenador (decisão 5 da 9.5a).</summary>
public class ServerUrlRulesTests
{
    private const string Invalid = "Informe um endereço http:// ou https:// completo.";
    private const string HttpsRequired = "Use https://. O http:// só vale para 127.0.0.1.";

    [Theory]
    [InlineData("https://sync.exemplo.com/")]
    [InlineData("https://192.168.0.10:8443/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://127.0.0.2/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("http://[::1]:8080/")]
    public void Accepts_https_anywhere_and_http_on_loopback(string address) =>
        ServerUrlRules.Check(new Uri(address)).Should().BeNull();

    [Theory]
    [InlineData("http://exemplo.com/")]
    [InlineData("http://192.168.0.10:8080/")]
    public void Http_outside_loopback_requires_https(string address) =>
        ServerUrlRules.Check(new Uri(address)).Should().Be(HttpsRequired);

    [Theory]
    [InlineData("ftp://x/")]
    [InlineData("file:///home/ana")]
    [InlineData("https://u:p@x/")]
    [InlineData("https://u@x/")]
    [InlineData("https://x/?a=1")]
    [InlineData("https://x/?")]
    [InlineData("https://x/#y")]
    [InlineData("https://x/#")]
    public void Rejects_other_schemes_credentials_query_and_fragment(string address) =>
        ServerUrlRules.Check(new Uri(address)).Should().Be(Invalid);

    [Fact]
    public void Rejects_a_relative_or_missing_address()
    {
        ServerUrlRules.Check(new Uri("/sync", UriKind.Relative)).Should().Be(Invalid);
        ServerUrlRules.Check(null).Should().Be(Invalid);
    }
}
