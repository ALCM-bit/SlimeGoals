using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SlimeGoals.Server.Tests.Support;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class ForwardedHeadersTests(PostgresFixture postgres)
{
    private static async Task<HttpResponseMessage> PreloginFrom(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/prelogin")
        {
            Content = JsonContent.Create(new PreloginRequest("a@exemplo.com"), SyncJsonContext.Default.PreloginRequest),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Trusted_proxy_forwarded_for_separates_clients()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
        {
            ["Server:TrustedProxyNetworks:0"] = "10.9.0.0/24",
            ["Limits:AuthPerIpPerMinute"] = "2",
        });
        using var proxied = factory.WithRemoteIp("10.9.0.2");
        using var client = proxied.CreateClient();

        foreach (var clientIp in new[] { "203.0.113.1", "203.0.113.2" })
        {
            for (var i = 0; i < 2; i++)
            {
                (await PreloginFrom(client, clientIp)).StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        (await PreloginFrom(client, "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Spoofed_forwarded_for_from_untrusted_peer_is_ignored()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerIpPerMinute"] = "2" });
        using var direct = factory.WithRemoteIp("198.51.100.7");
        using var client = direct.CreateClient();

        (await PreloginFrom(client, "203.0.113.1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PreloginFrom(client, "203.0.113.2")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PreloginFrom(client, "203.0.113.3")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Invalid_trusted_proxy_network_fails_startup()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Server:TrustedProxyNetworks:0"] = "isto-nao-e-cidr" });

        var start = () => factory.CreateClient();

        start.Should().Throw<Exception>().Which.ToString().Should().Contain("TrustedProxyNetworks");
    }
}
