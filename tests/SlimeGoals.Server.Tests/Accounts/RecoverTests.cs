using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class RecoverTests(PostgresFixture postgres)
{
    private static readonly byte[] WrappedRecovery = [9, 8, 7];

    private static RecoverRequest NewRecover(string email, byte[] recoveryKey) => new(
        email,
        recoveryKey,
        RandomNumberGenerator.GetBytes(32),
        RandomNumberGenerator.GetBytes(16),
        "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}",
        [42, 43, 44],
        RandomNumberGenerator.GetBytes(32),
        [7, 6, 5, 4]);

    private static Task<HttpResponseMessage> BeginAsync(HttpClient client, string email, byte[] recoveryKey) =>
        client.PostAsJsonAsync("/auth/recover/begin", new RecoverBeginRequest(email, recoveryKey), SyncJsonContext.Default.RecoverBeginRequest);

    private static Task<HttpResponseMessage> RecoverAsync(HttpClient client, RecoverRequest request) =>
        client.PostAsJsonAsync("/auth/recover", request, SyncJsonContext.Default.RecoverRequest);

    [Fact]
    public async Task Recover_begin_returns_the_wrapped_key_only_with_the_right_code()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var right = await BeginAsync(client, account.Email.ToUpperInvariant(), account.RecoveryKey);
        var wrong = await BeginAsync(client, account.Email, RandomNumberGenerator.GetBytes(32));
        var unknown = await BeginAsync(client, "ninguem@exemplo.com", account.RecoveryKey);

        right.StatusCode.Should().Be(HttpStatusCode.OK);
        (await right.Content.ReadFromJsonAsync(SyncJsonContext.Default.RecoverBeginResponse))!.WrappedKeyRecovery.Should().Equal(WrappedRecovery);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrong.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recover_with_unconfirmed_account_answers_the_same_401_as_unknown_email()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();
        var register = TestAccount.NewRegisterRequest("pendente@exemplo.com");
        (await client.PostAsJsonAsync("/auth/register", register, SyncJsonContext.Default.RegisterRequest)).EnsureSuccessStatusCode();

        var begin = await BeginAsync(client, "pendente@exemplo.com", register.RecoveryKey!);
        var beginUnknown = await BeginAsync(client, "ninguem@exemplo.com", register.RecoveryKey!);
        var recover = await RecoverAsync(client, NewRecover("pendente@exemplo.com", register.RecoveryKey!));
        var recoverUnknown = await RecoverAsync(client, NewRecover("ninguem@exemplo.com", register.RecoveryKey!));

        begin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        recover.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await begin.Content.ReadAsStringAsync()).Should().Be(await beginUnknown.Content.ReadAsStringAsync());
        (await recover.Content.ReadAsStringAsync()).Should().Be(await recoverUnknown.Content.ReadAsStringAsync());
        (await recover.Content.ReadAsStringAsync()).Should().Be(await begin.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Recover_with_wrong_code_answers_401_without_calling_begin_and_changes_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();
        var request = NewRecover(account.Email, RandomNumberGenerator.GetBytes(32));

        var response = await RecoverAsync(client, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PasswordTests.LoginAsync(factory, account.Email, request.AuthKey!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PasswordTests.LoginAsync(factory, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Recover_sets_the_password_and_revokes_all_devices()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");
        using var client = factory.CreateClient();
        var request = NewRecover(account.Email, account.RecoveryKey);

        var response = await RecoverAsync(client, request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PasswordTests.LoginAsync(factory, account.Email, account.AuthKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await PasswordTests.LoginAsync(factory, account.Email, request.AuthKey!);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync(SyncJsonContext.Default.LoginResponse))!.WrappedKeyPassword.Should().Equal(request.WrappedKeyPassword);
        var prelogin = await client.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(account.Email), SyncJsonContext.Default.PreloginRequest);
        (await prelogin.Content.ReadFromJsonAsync(SyncJsonContext.Default.PreloginResponse))!.KdfSalt.Should().Equal(request.KdfSalt);

        // O código usado deixa de valer; o novo abre a chave cifrada por ele.
        (await BeginAsync(client, account.Email, account.RecoveryKey)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var again = await BeginAsync(client, account.Email, request.NewRecoveryKey!);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync(SyncJsonContext.Default.RecoverBeginResponse))!.WrappedKeyRecovery
            .Should().Equal(request.NewWrappedKeyRecovery);
    }

    [Fact]
    public async Task Recovery_code_works_only_once()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();
        var first = NewRecover(account.Email, account.RecoveryKey);
        (await RecoverAsync(client, first)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = NewRecover(account.Email, account.RecoveryKey);
        var response = await RecoverAsync(client, second);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PasswordTests.LoginAsync(factory, account.Email, second.AuthKey!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PasswordTests.LoginAsync(factory, account.Email, first.AuthKey!)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BeginAsync(client, account.Email, second.NewRecoveryKey!)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Two_recoveries_at_once_with_the_same_code_let_only_one_through()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();
        var requests = Enumerable.Range(0, 6).Select(_ => NewRecover(account.Email, account.RecoveryKey)).ToArray();

        var responses = await Task.WhenAll(requests.Select(r => RecoverAsync(client, r)));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        var winner = requests[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.NoContent)];
        (await PasswordTests.LoginAsync(factory, account.Email, winner.AuthKey!)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await BeginAsync(client, account.Email, winner.NewRecoveryKey!)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Recover_with_invalid_body_answers_400()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        using var client = factory.CreateClient();

        var shortKey = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { AuthKey = [1] });
        var control = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { KdfParams = "a\u0000b" });
        var noNewKey = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { NewRecoveryKey = null });
        var shortNewKey = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { NewRecoveryKey = [1, 2] });
        var noNewWrap = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { NewWrappedKeyRecovery = [] });
        var sameCode = await RecoverAsync(client, NewRecover(account.Email, account.RecoveryKey) with { NewRecoveryKey = account.RecoveryKey });
        var beginMalformed = await client.PostAsync("/auth/recover/begin", new StringContent("[", Encoding.UTF8, "application/json"));
        var beginShort = await BeginAsync(client, account.Email, [1, 2]);

        shortKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        control.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        beginMalformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        beginShort.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noNewKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        shortNewKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noNewWrap.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        sameCode.StatusCode.Should().Be(HttpStatusCode.BadRequest, "trocar o código pelo mesmo não é trocar");

        // Nenhum desses pedidos mexeu na conta: o código continua valendo.
        (await BeginAsync(client, account.Email, account.RecoveryKey)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
