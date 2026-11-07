using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class LoginTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, byte[] authKey) =>
        client.PostAsJsonAsync("/auth/login", new LoginRequest(email, authKey, "pc"), SyncJsonContext.Default.LoginRequest);

    [Fact]
    public async Task Login_before_confirming_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest);

        var response = await LoginAsync(client, "ana@exemplo.com", request.AuthKey!);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
        var unknown = await LoginAsync(client, "ninguem@exemplo.com", request.AuthKey!);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("a\u0000@b.com", "pc")]
    [InlineData("a\r\n@b.com", "pc")]
    [InlineData("ana@exemplo.com", "pc\u0000")]
    [InlineData("ana@exemplo.com", "pc\r\nX: y")]
    public async Task Login_with_control_characters_answers_invalid_request(string email, string device)
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(email, RandomNumberGenerator.GetBytes(32), device), SyncJsonContext.Default.LoginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Login_with_wrong_key_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, account.Email, RandomNumberGenerator.GetBytes(32));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Login_with_unknown_email_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var wrongKey = await LoginAsync(client, account.Email, RandomNumberGenerator.GetBytes(32));
        var unknown = await LoginAsync(client, "ninguem@exemplo.com", RandomNumberGenerator.GetBytes(32));

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await wrongKey.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_creates_a_device_and_returns_the_wrapped_key()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(account.Email, account.AuthKey, "Notebook"), SyncJsonContext.Default.LoginRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.LoginResponse))!;
        body.WrappedKeyPassword.Should().Equal(TestAccount.WrappedPassword);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        var row = await connection.QuerySingleAsync(
            "SELECT name, token_hash, last_seen_at, revoked_at FROM devices WHERE id = @id", new { id = body.DeviceId });
        ((string)row.name).Should().Be("Notebook");
        ((byte[])row.token_hash).Should().Equal(SHA256.HashData(Encoding.UTF8.GetBytes(body.Token)));
        Encoding.UTF8.GetBytes(body.Token).Should().NotEqual((byte[])row.token_hash);
        ((object?)row.revoked_at).Should().BeNull();
    }

    [Fact]
    public async Task Logout_revokes_only_this_device()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");

        var logout = await account.First.Client.PostAsync("/auth/logout", null);
        var firstAfter = await account.First.Client.GetAsync(ServerFactory.ProtectedTestRoute);
        var secondAfter = await second.Client.GetAsync(ServerFactory.ProtectedTestRoute);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        firstAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        secondAfter.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Request_without_token_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var logout = await client.PostAsync("/auth/logout", null);
        var route = await client.GetAsync(ServerFactory.ProtectedTestRoute);
        using var bad = factory.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-que-nao-existe");
        var unknown = await bad.GetAsync(ServerFactory.ProtectedTestRoute);

        logout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await logout.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
        route.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Last_seen_is_updated_at_most_once_per_hour()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var start = await LastSeenAsync(factory, account.First.DeviceId);

        factory.Time.Advance(TimeSpan.FromMinutes(30));
        await account.Client.GetAsync(ServerFactory.ProtectedTestRoute);
        var afterHalfHour = await LastSeenAsync(factory, account.First.DeviceId);
        factory.Time.Advance(TimeSpan.FromMinutes(31));
        await account.Client.GetAsync(ServerFactory.ProtectedTestRoute);
        var afterHour = await LastSeenAsync(factory, account.First.DeviceId);

        afterHalfHour.Should().Be(start);
        afterHour.Should().BeAfter(start);
    }

    private static async Task<DateTime> LastSeenAsync(ServerFactory factory, Guid deviceId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<DateTime>("SELECT last_seen_at FROM devices WHERE id = @deviceId", new { deviceId });
    }
}
