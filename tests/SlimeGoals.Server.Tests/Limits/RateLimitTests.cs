using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dapper;
using Npgsql;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Server.Tests.Support;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class RateLimitTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> PreloginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, byte[] key) =>
        client.PostAsJsonAsync("/auth/login", new LoginRequest(email, key, "pc"), SyncJsonContext.Default.LoginRequest);

    private static async Task ShouldBeRateLimitedAsync(HttpResponseMessage response, int maxSeconds = 60)
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, maxSeconds);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("rate_limited");
    }

    [Fact]
    public async Task Auth_routes_answer_429_past_the_ip_limit()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerIpPerMinute"] = "3" });
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await PreloginAsync(client, $"a{i}@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ShouldBeRateLimitedAsync(await PreloginAsync(client, "outro@exemplo.com"));
        await ShouldBeRateLimitedAsync(await LoginAsync(client, "outro@exemplo.com", RandomNumberGenerator.GetBytes(32)));
    }

    private static Dictionary<string, string?> ProxiedSettings(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string?> { ["Server:TrustedProxyNetworks:0"] = "10.9.0.0/24" };
        foreach (var (key, value) in more)
        {
            settings[key] = value;
        }

        return settings;
    }

    /// <summary>Um cliente que chega pelo proxy confiável como se fosse o IP dado.</summary>
    private static HttpClient ClientFrom(HttpClient proxied, string ip)
    {
        proxied.DefaultRequestHeaders.Remove("X-Forwarded-For");
        proxied.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return proxied;
    }

    [Fact]
    public async Task Prelogin_flood_from_other_ips_does_not_block_victim_login()
    {
        await using var factory = new ServerFactory(postgres, ProxiedSettings());
        var victim = await TestAccount.CreateAsync(factory);
        using var proxied = factory.WithRemoteIp("10.9.0.2");

        for (var i = 0; i < 20; i++)
        {
            using var attacker = proxied.CreateClient();
            (await PreloginAsync(ClientFrom(attacker, $"203.0.113.{i + 1}"), victim.Email)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var home = proxied.CreateClient();
        (await LoginAsync(ClientFrom(home, "198.51.100.50"), victim.Email, victim.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_ip_limit_is_per_pair()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerEmailPerIpPerMinute"] = "2" });
        using var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            (await PreloginAsync(client, "a@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await PreloginAsync(client, "b@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ShouldBeRateLimitedAsync(await PreloginAsync(client, "a@exemplo.com"));
    }

    [Fact]
    public async Task Login_failures_per_email_lock_after_limit_from_any_ip()
    {
        await using var factory = new ServerFactory(postgres, ProxiedSettings(("Limits:LoginFailuresPerEmailPerHour", "3")));
        var account = await TestAccount.CreateAsync(factory);
        using var proxied = factory.WithRemoteIp("10.9.0.2");
        var wrong = RandomNumberGenerator.GetBytes(32);

        for (var i = 0; i < 3; i++)
        {
            using var attacker = proxied.CreateClient();
            (await LoginAsync(ClientFrom(attacker, $"203.0.113.{i + 1}"), account.Email.ToUpperInvariant(), wrong))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var home = proxied.CreateClient();
        ClientFrom(home, "198.51.100.50");
        await ShouldBeRateLimitedAsync(await LoginAsync(home, account.Email, account.AuthKey), maxSeconds: 3600);

        factory.Time.Advance(TimeSpan.FromHours(1));
        (await LoginAsync(home, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_email_spends_the_login_failure_budget_too()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:LoginFailuresPerEmailPerHour"] = "2" });
        using var client = factory.CreateClient();
        var wrong = RandomNumberGenerator.GetBytes(32);

        for (var i = 0; i < 2; i++)
        {
            (await LoginAsync(client, "ninguem@exemplo.com", wrong)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await ShouldBeRateLimitedAsync(await LoginAsync(client, "ninguem@exemplo.com", wrong), maxSeconds: 3600);
    }

    [Fact]
    public async Task Successful_login_does_not_spend_failure_budget()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:LoginFailuresPerEmailPerHour"] = "2" });
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(client, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Confirmation_email_capped_per_address_per_hour()
    {
        await using var factory = new ServerFactory(postgres, ProxiedSettings(("Limits:ConfirmationEmailsPerAddressPerHour", "3")));
        using var proxied = factory.WithRemoteIp("10.9.0.2");
        var email = $"alvo{Guid.NewGuid():N}@exemplo.com";

        for (var i = 0; i < 4; i++)
        {
            using var client = proxied.CreateClient();
            ClientFrom(client, $"203.0.113.{i + 1}");
            (await client.PostAsJsonAsync("/auth/register", TestAccount.NewRegisterRequest(email), SyncJsonContext.Default.RegisterRequest))
                .StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        await proxied.WaitForEmailQueueAsync();
        factory.Email.Count.Should().Be(3);
    }

    [Fact]
    public async Task Limit_resets_after_a_minute()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerIpPerMinute"] = "2" });
        using var client = factory.CreateClient();
        await PreloginAsync(client, "a@exemplo.com");
        await PreloginAsync(client, "a@exemplo.com");
        await ShouldBeRateLimitedAsync(await PreloginAsync(client, "a@exemplo.com"));

        factory.Time.Advance(TimeSpan.FromSeconds(30));
        (await PreloginAsync(client, "a@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        factory.Time.Advance(TimeSpan.FromSeconds(31));

        (await PreloginAsync(client, "a@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task All_routes_answer_429_past_the_request_ip_limit()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:RequestsPerIpPerMinute"] = "3" });
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await client.GetAsync("/devices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await ShouldBeRateLimitedAsync(await client.GetAsync("/devices"));
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sync_answers_429_past_the_device_limit()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:SyncPerDevicePerMinute"] = "2" });
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");

        for (var i = 0; i < 2; i++)
        {
            (await account.Client.GetAsync("/sync/pull")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await ShouldBeRateLimitedAsync(await account.Client.GetAsync("/sync/pull"));
        (await second.Client.GetAsync("/sync/pull")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sync_routes_have_no_auth_rate_limit()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerIpPerMinute"] = "3" });
        // Cadastro, confirmação e entrada gastam os 3 pedidos de /auth; o sync não conta.
        var account = await TestAccount.CreateAsync(factory);

        for (var i = 0; i < 5; i++)
        {
            (await account.Client.GetAsync("/sync/pull")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Wrong_auth_key_answers_429_past_the_account_limit_on_delete_account()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthKeyFailuresPerAccountPerMinute"] = "3" });
        var account = await TestAccount.CreateAsync(factory);

        for (var i = 0; i < 3; i++)
        {
            (await DeleteAsync(account.Client, RandomNumberGenerator.GetBytes(32))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await ShouldBeRateLimitedAsync(await DeleteAsync(account.Client, RandomNumberGenerator.GetBytes(32)));
        await ShouldBeRateLimitedAsync(await DeleteAsync(account.Client, account.AuthKey));
        (await CountAccountsAsync(factory, account.Email)).Should().Be(1);

        factory.Time.Advance(TimeSpan.FromSeconds(61));
        (await DeleteAsync(account.Client, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Wrong_auth_key_answers_429_past_the_account_limit_on_change_password()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthKeyFailuresPerAccountPerMinute"] = "3" });
        var account = await TestAccount.CreateAsync(factory);
        var newKey = RandomNumberGenerator.GetBytes(32);

        for (var i = 0; i < 3; i++)
        {
            var wrong = PasswordTests.NewPassword(RandomNumberGenerator.GetBytes(32), newKey);
            (await account.Client.PostAsJsonAsync("/auth/password", wrong, SyncJsonContext.Default.ChangePasswordRequest))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var right = PasswordTests.NewPassword(account.AuthKey, newKey);
        await ShouldBeRateLimitedAsync(
            await account.Client.PostAsJsonAsync("/auth/password", right, SyncJsonContext.Default.ChangePasswordRequest));
        (await LoginAsync(factory.CreateClient(), account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(factory.CreateClient(), account.Email, newKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("password")]
    public async Task Concurrent_wrong_keys_never_get_past_the_account_limit(string route)
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthKeyFailuresPerAccountPerMinute"] = "3" });
        var account = await TestAccount.CreateAsync(factory);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => route == "delete"
            ? DeleteAsync(account.Client, RandomNumberGenerator.GetBytes(32))
            : account.Client.PostAsJsonAsync(
                "/auth/password", PasswordTests.NewPassword(RandomNumberGenerator.GetBytes(32)), SyncJsonContext.Default.ChangePasswordRequest))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().BeLessThanOrEqualTo(3);
        responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(17);
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, byte[] authKey) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(authKey), SyncJsonContext.Default.DeleteAccountRequest),
        });

    private static async Task<int> CountAccountsAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM accounts WHERE email = @email", new { email });
    }
}
