using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Dapper;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class DeleteAccountTests(PostgresFixture postgres)
{
    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, byte[]? authKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(authKey), SyncJsonContext.Default.DeleteAccountRequest),
        };
        return client.SendAsync(request);
    }

    private static async Task<int> CountAsync(ServerFactory factory, string table, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        var sql = table == "accounts"
            ? "SELECT count(*) FROM accounts WHERE email = @email"
            : $"SELECT count(*) FROM {table} t JOIN accounts a ON a.id = t.account_id WHERE a.email = @email";
        return await connection.ExecuteScalarAsync<int>(sql, new { email });
    }

    private static async Task AddRecordsAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO records (account_id, record_key, seq, blob) SELECT id, 'k1', 1, @blob FROM accounts WHERE email = @email",
            new { email, blob = new byte[] { 1, 2, 3 } });
    }

    [Fact]
    public async Task Delete_account_with_wrong_key_answers_401_and_keeps_everything()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await AddRecordsAsync(factory, account.Email);

        var wrong = await DeleteAsync(account.Client, RandomNumberGenerator.GetBytes(32));
        var missing = await DeleteAsync(account.Client, null);

        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountAsync(factory, "accounts", account.Email)).Should().Be(1);
        (await CountAsync(factory, "devices", account.Email)).Should().Be(1);
        (await CountAsync(factory, "records", account.Email)).Should().Be(1);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_account_without_token_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var response = await DeleteAsync(client, account.AuthKey);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CountAsync(factory, "accounts", account.Email)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_account_removes_records_and_devices()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var other = await TestAccount.CreateAsync(factory);
        await account.LoginAsync("segundo");
        await AddRecordsAsync(factory, account.Email);
        await AddRecordsAsync(factory, other.Email);

        var response = await DeleteAsync(account.Client, account.AuthKey);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CountAsync(factory, "accounts", account.Email)).Should().Be(0);
        (await CountAsync(factory, "devices", account.Email)).Should().Be(0);
        (await CountAsync(factory, "records", account.Email)).Should().Be(0);
        (await CountAsync(factory, "records", other.Email)).Should().Be(1);
        (await other.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var client = factory.CreateClient();
        var again = await client.PostAsJsonAsync(
            "/auth/register", TestAccount.NewRegisterRequest(account.Email), SyncJsonContext.Default.RegisterRequest);
        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.LastEmail.Should().Be(account.Email);
    }
}
