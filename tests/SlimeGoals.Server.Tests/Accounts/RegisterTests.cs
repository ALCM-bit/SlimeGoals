using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class RegisterTests(PostgresFixture postgres)
{
    private const string DefaultParams = "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequest request) =>
        client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest);

    private static Task<HttpResponseMessage> PreloginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest);

    [Fact]
    public async Task Prelogin_returns_the_salt_of_the_account()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        (await RegisterAsync(client, request)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var response = await PreloginAsync(client, "ana@exemplo.com");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse))!;
        body.KdfSalt.Should().Equal(request.KdfSalt);
        body.KdfParams.Should().Be(request.KdfParams);
    }

    [Fact]
    public async Task Prelogin_of_unknown_email_is_stable_and_has_the_same_shape()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Server:PreloginSecret"] = "segredo" });
        using var client = factory.CreateClient();

        var first = await (await PreloginAsync(client, "ninguem@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);
        var second = await (await PreloginAsync(client, "ninguem@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);
        var other = await (await PreloginAsync(client, "outro@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);

        first!.KdfSalt.Should().HaveCount(16);
        first.KdfSalt.Should().Equal(second!.KdfSalt);
        first.KdfSalt.Should().NotEqual(other!.KdfSalt);
        first.KdfParams.Should().Be(DefaultParams);
    }

    [Fact]
    public async Task Register_answers_202_and_sends_confirmation()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Count.Should().Be(1);
        factory.Email.LastEmail.Should().Be("ana@exemplo.com");
        factory.Email.LastToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Register_with_used_email_answers_202_and_changes_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));
        var before = await ReadAccountAsync(factory, "ana@exemplo.com");

        var response = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Count.Should().Be(1);
        var after = await ReadAccountAsync(factory, "ana@exemplo.com");
        ((byte[])after.kdf_salt).Should().Equal((byte[])before.kdf_salt);
        ((byte[])after.auth_hash).Should().Equal((byte[])before.auth_hash);
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Register_of_a_confirmed_email_answers_202_and_changes_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var before = await ReadAccountAsync(factory, account.Email);
        var sent = factory.Email.Count;
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, TestAccount.NewRegisterRequest(account.Email));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Count.Should().Be(sent);
        var after = await ReadAccountAsync(factory, account.Email);
        ((byte[])after.auth_hash).Should().Equal((byte[])before.auth_hash);
    }

    [Fact]
    public async Task Simultaneous_registers_of_the_same_email_create_one_account()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"))));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        (await CountAccountsAsync(factory)).Should().Be(1);
        factory.Email.Count.Should().Be(1);
    }

    [Fact]
    public async Task Email_is_normalized()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);
        await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(factory.Email.LastToken), SyncJsonContext.Default.ConfirmRequest);

        var prelogin = await PreloginAsync(client, "  Ana@Exemplo.COM ");
        var login = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("  Ana@Exemplo.COM ", request.AuthKey, "pc"), SyncJsonContext.Default.LoginRequest);

        (await prelogin.Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse))!.KdfSalt.Should().Equal(request.KdfSalt);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await RegisterAsync(client, TestAccount.NewRegisterRequest("ANA@exemplo.com "));
        again.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Confirm_with_unknown_or_expired_token_answers_400()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));
        var token = factory.Email.LastToken;

        var unknown = await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest("nada"), SyncJsonContext.Default.ConfirmRequest);
        factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var expired = await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(token), SyncJsonContext.Default.ConfirmRequest);

        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_token");
        expired.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await expired.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_token");
    }

    [Fact]
    public async Task Confirm_works_once_and_then_login_works()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);
        var token = factory.Email.LastToken;

        var first = await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(token), SyncJsonContext.Default.ConfirmRequest);
        var second = await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(token), SyncJsonContext.Default.ConfirmRequest);
        var login = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("ana@exemplo.com", request.AuthKey, "pc"), SyncJsonContext.Default.LoginRequest);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Stored_account_has_no_plain_secret()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);

        var row = await ReadAccountAsync(factory, "ana@exemplo.com");

        ((byte[])row.auth_hash).Should().NotEqual(request.AuthKey).And.HaveCount(32);
        ((byte[])row.recovery_hash).Should().NotEqual(request.RecoveryKey).And.HaveCount(32);
        ((byte[])row.wrapped_key_password).Should().Equal(request.WrappedKeyPassword);
        ((byte[])row.wrapped_key_recovery).Should().Equal(request.WrappedKeyRecovery);
    }

    [Fact]
    public async Task Malformed_body_or_missing_field_answers_invalid_request()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var broken = await client.PostAsync("/auth/register", new StringContent("{nao e json", System.Text.Encoding.UTF8, "application/json"));
        var missing = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com") with { AuthKey = null });
        var shortKey = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com") with { AuthKey = new byte[5] });

        foreach (var response in new[] { broken, missing, shortKey })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
        }
    }

    [Fact]
    public async Task Production_refuses_to_start_with_only_the_log_email_sender()
    {
        await using var factory = new ServerFactory(
            postgres,
            new Dictionary<string, string?> { ["Server:PreloginSecret"] = "segredo" },
            environment: "Production");

        var act = () => factory.CreateClient();

        act.Should().Throw<Exception>().Which.Message.Should().Contain("IEmailSender");
    }

    private static async Task<dynamic> ReadAccountAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.QuerySingleAsync("SELECT * FROM accounts WHERE email = @email", new { email });
    }

    private static async Task<int> CountAccountsAsync(ServerFactory factory)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM accounts");
    }
}
