using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class RegisterTests(PostgresFixture postgres)
{
    private const string DefaultParams = "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}";

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequest request) =>
        client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string token, byte[]? authKey) =>
        client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(token, authKey), SyncJsonContext.Default.ConfirmRequest);

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, byte[]? authKey) =>
        client.PostAsJsonAsync("/auth/login", new LoginRequest(email, authKey, "pc"), SyncJsonContext.Default.LoginRequest);

    private static Task<HttpResponseMessage> PreloginAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest);

    [Fact]
    public async Task Prelogin_returns_the_salt_of_the_account()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        (await RegisterAsync(client, request)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await ConfirmAsync(client, factory.Email.LastToken, request.AuthKey);

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
    public async Task Prelogin_of_unconfirmed_account_returns_the_fake_salt()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Server:PreloginSecret"] = "segredo" });
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        var expected = await (await PreloginAsync(client, "ana@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);
        await RegisterAsync(client, request);

        var pending = await (await PreloginAsync(client, "ana@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);
        await ConfirmAsync(client, factory.Email.LastToken, request.AuthKey);
        var confirmed = await (await PreloginAsync(client, "ana@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);

        pending!.KdfSalt.Should().Equal(expected!.KdfSalt);
        pending.KdfParams.Should().Be(expected.KdfParams);
        confirmed!.KdfSalt.Should().Equal(request.KdfSalt);
    }

    [Theory]
    [InlineData("a\u0000@b.com")]
    [InlineData("a\r\nBcc: x@y.com@b.com")]
    [InlineData("a b@b.com")]
    [InlineData("ana@")]
    [InlineData("@exemplo.com")]
    [InlineData("ana@exemplo")]
    [InlineData("ana@@exemplo.com")]
    [InlineData("ana@exemplo..com")]
    public async Task Prelogin_and_register_with_a_malformed_email_answer_invalid_request(string email)
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var prelogin = await PreloginAsync(client, email);
        var register = await RegisterAsync(client, TestAccount.NewRegisterRequest(email));

        foreach (var response in new[] { prelogin, register })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
        }

        factory.Email.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("{\"v\":1}\u0000")]
    [InlineData("{\"v\":1}\r\n")]
    public async Task Register_with_control_characters_in_kdf_params_answers_invalid_request(string kdfParams)
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        var response = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com") with { KdfParams = kdfParams });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
    }

    [Fact]
    public async Task Failed_email_send_removes_the_account_so_the_email_can_register_again()
    {
        await using var factory = new ServerFactory(postgres);
        var sender = new ThrowingSender { Fail = true };
        await using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureServices(
            services => services.AddSingleton<SlimeGoals.Server.Accounts.IEmailSender>(sender)));
        using var client = failing.CreateClient();

        var first = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));
        var afterFailure = await CountAccountsAsync(factory);
        sender.Fail = false;
        var second = await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));

        first.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        afterFailure.Should().Be(0);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        sender.Sent.Should().Be(1);
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    private sealed class ThrowingSender : SlimeGoals.Server.Accounts.IEmailSender
    {
        public bool Fail { get; set; }

        public int Sent { get; private set; }

        public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvalidOperationException("falha simulada");
            }

            Sent++;
            return Task.CompletedTask;
        }
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
    public async Task Register_of_a_pending_email_replaces_the_pending_account_and_sends_a_new_email()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));
        var before = await ReadAccountAsync(factory, "ana@exemplo.com");
        var second = TestAccount.NewRegisterRequest("ana@exemplo.com");

        var response = await RegisterAsync(client, second);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.Email.Count.Should().Be(2);
        factory.Email.LastEmail.Should().Be("ana@exemplo.com");
        var after = await ReadAccountAsync(factory, "ana@exemplo.com");
        ((byte[])after.kdf_salt).Should().Equal(second.KdfSalt);
        ((byte[])after.auth_hash).Should().NotEqual((byte[])before.auth_hash);
        ((byte[])after.confirm_hash).Should().NotEqual((byte[])before.confirm_hash);
        ((object?)after.email_confirmed_at).Should().BeNull();
        (await CountAccountsAsync(factory)).Should().Be(1);
    }

    [Fact]
    public async Task Attacker_registering_first_cannot_take_over_the_victims_email()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var attacker = TestAccount.NewRegisterRequest("vitima@exemplo.com");
        var victim = TestAccount.NewRegisterRequest("vitima@exemplo.com");

        await RegisterAsync(client, attacker);
        var attackerToken = factory.Email.LastToken;
        (await RegisterAsync(client, victim)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var victimToken = factory.Email.LastToken;

        victimToken.Should().NotBe(attackerToken);
        (await ConfirmAsync(client, attackerToken, attacker.AuthKey)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ConfirmAsync(client, victimToken, victim.AuthKey)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync(client, "vitima@exemplo.com", victim.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(client, "vitima@exemplo.com", attacker.AuthKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var prelogin = await (await PreloginAsync(client, "vitima@exemplo.com")).Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse);
        prelogin!.KdfSalt.Should().Equal(victim.KdfSalt);
    }

    [Fact]
    public async Task Confirm_with_the_right_token_but_another_auth_key_answers_invalid_token()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);
        var token = factory.Email.LastToken;

        var wrong = await ConfirmAsync(client, token, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_token");
        var row = await ReadAccountAsync(factory, "ana@exemplo.com");
        ((object?)row.email_confirmed_at).Should().BeNull();
        (await LoginAsync(client, "ana@exemplo.com", request.AuthKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ConfirmAsync(client, token, request.AuthKey)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Old_token_stops_working_after_a_new_register_even_with_its_own_auth_key()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var first = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, first);
        var oldToken = factory.Email.LastToken;
        var second = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, second);

        var withOldKey = await ConfirmAsync(client, oldToken, first.AuthKey);
        var withNewKey = await ConfirmAsync(client, oldToken, second.AuthKey);

        withOldKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await withOldKey.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_token");
        withNewKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ((object?)(await ReadAccountAsync(factory, "ana@exemplo.com")).email_confirmed_at).Should().BeNull();
    }

    [Fact]
    public async Task Confirm_without_a_valid_auth_key_answers_invalid_request()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        await RegisterAsync(client, TestAccount.NewRegisterRequest("ana@exemplo.com"));
        var token = factory.Email.LastToken;

        foreach (var key in new[] { null, new byte[5] })
        {
            var response = await ConfirmAsync(client, token, key);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("invalid_request");
        }
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
        ((byte[])after.kdf_salt).Should().Equal((byte[])before.kdf_salt);
        ((object?)after.confirm_hash).Should().BeNull();
        (await LoginAsync(client, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Simultaneous_registers_of_the_same_email_leave_one_whole_pending_account()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var requests = Enumerable.Range(0, 8).Select(_ => TestAccount.NewRegisterRequest("ana@exemplo.com")).ToArray();

        var responses = await Task.WhenAll(requests.Select(r => RegisterAsync(client, r)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);
        (await CountAccountsAsync(factory)).Should().Be(1);
        factory.Email.Count.Should().Be(8);

        // A linha final é inteira de um só cadastro: o sal e a auth_key são do mesmo pedido.
        var row = await ReadAccountAsync(factory, "ana@exemplo.com");
        var winner = requests.Single(r => ((byte[])row.kdf_salt).SequenceEqual(r.KdfSalt!));
        ((byte[])row.auth_hash).Should().Equal(SlimeGoals.Server.Accounts.SecretHash.Of(winner.AuthKey!));
        ((byte[])row.recovery_hash).Should().Equal(SlimeGoals.Server.Accounts.SecretHash.Of(winner.RecoveryKey!));
    }

    [Fact]
    public async Task Email_is_normalized()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);
        await ConfirmAsync(client, factory.Email.LastToken, request.AuthKey);

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
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");
        await RegisterAsync(client, request);
        var token = factory.Email.LastToken;

        var unknown = await ConfirmAsync(client, "nada", request.AuthKey);
        factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        var expired = await ConfirmAsync(client, token, request.AuthKey);

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

        var first = await ConfirmAsync(client, token, request.AuthKey);
        var second = await ConfirmAsync(client, token, request.AuthKey);
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
