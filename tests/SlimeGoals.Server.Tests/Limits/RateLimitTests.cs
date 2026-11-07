using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dapper;
using Npgsql;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class RateLimitTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> PreloginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, byte[] key) =>
        client.PostAsJsonAsync("/auth/login", new LoginRequest(email, key, "pc"), SyncJsonContext.Default.LoginRequest);

    private static async Task ShouldBeRateLimitedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        response.Headers.RetryAfter!.Delta!.Value.TotalSeconds.Should().BeInRange(1, 60);
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

    [Fact]
    public async Task Login_answers_429_past_the_email_limit_from_any_ip()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Limits:AuthPerEmailPerMinute"] = "5" });
        // Cadastro e primeira entrada já gastaram 2 dos 5 pedidos do e-mail.
        var account = await TestAccount.CreateAsync(factory);
        var wrong = RandomNumberGenerator.GetBytes(32);

        for (var i = 0; i < 3; i++)
        {
            // Cada pedido sai de um IP diferente: o limite por e-mail não olha o IP.
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.0.0.{i}");
            (await LoginAsync(client, account.Email.ToUpperInvariant(), wrong)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var another = factory.CreateClient();
        await ShouldBeRateLimitedAsync(await LoginAsync(another, account.Email, account.AuthKey));
        await ShouldBeRateLimitedAsync(await PreloginAsync(another, account.Email));
        (await LoginAsync(another, "outra@exemplo.com", wrong)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
