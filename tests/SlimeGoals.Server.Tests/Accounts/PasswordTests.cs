using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class PasswordTests(PostgresFixture postgres)
{
    internal static ChangePasswordRequest NewPassword(byte[] currentKey, byte[]? newKey = null) => new(
        currentKey,
        newKey ?? RandomNumberGenerator.GetBytes(32),
        RandomNumberGenerator.GetBytes(16),
        "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}",
        [42, 43, 44]);

    internal static Task<HttpResponseMessage> LoginAsync(ServerFactory factory, string email, byte[] authKey)
    {
        var client = factory.CreateClient();
        return client.PostAsJsonAsync("/auth/login", new LoginRequest(email, authKey, "pc"), SyncJsonContext.Default.LoginRequest);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_key_answers_401_and_changes_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");
        var request = NewPassword(RandomNumberGenerator.GetBytes(32));

        var response = await account.Client.PostAsJsonAsync("/auth/password", request, SyncJsonContext.Default.ChangePasswordRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
        (await LoginAsync(factory, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(factory, account.Email, request.AuthKey!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        (await connection.ExecuteScalarAsync<byte[]>("SELECT wrapped_key_password FROM accounts WHERE email = @e", new { e = account.Email }))
            .Should().Equal(TestAccount.WrappedPassword);
    }

    [Fact]
    public async Task Change_password_revokes_other_devices_and_keeps_this_one()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var other = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");
        var request = NewPassword(account.AuthKey);

        var response = await account.Client.PostAsJsonAsync("/auth/password", request, SyncJsonContext.Default.ChangePasswordRequest);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await other.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(factory, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var fresh = await LoginAsync(factory, account.Email, request.AuthKey!);
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fresh.Content.ReadFromJsonAsync(SyncJsonContext.Default.LoginResponse))!.WrappedKeyPassword.Should().Equal(request.WrappedKeyPassword);
        using var anonymous = factory.CreateClient();
        var prelogin = await anonymous.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(account.Email), SyncJsonContext.Default.PreloginRequest);
        (await prelogin.Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse))!.KdfSalt.Should().Equal(request.KdfSalt);
    }

    [Fact]
    public async Task Change_password_with_invalid_body_answers_400()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var shortKey = await account.Client.PostAsJsonAsync(
            "/auth/password", NewPassword(account.AuthKey, [1, 2]), SyncJsonContext.Default.ChangePasswordRequest);
        var control = await account.Client.PostAsJsonAsync(
            "/auth/password", NewPassword(account.AuthKey) with { KdfParams = "{}\r\n" }, SyncJsonContext.Default.ChangePasswordRequest);
        var malformed = await account.Client.PostAsync("/auth/password", new StringContent("{nao e json", Encoding.UTF8, "application/json"));

        shortKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        control.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoginAsync(factory, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Change_password_without_token_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/password", NewPassword(RandomNumberGenerator.GetBytes(32)), SyncJsonContext.Default.ChangePasswordRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
