using System.Net;
using System.Security.Cryptography;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Account;

public sealed class HttpAccountApiTests(PostgresFixture postgres)
{
    private const string Kdf = "{\"v\":1,\"m\":65536,\"t\":3,\"p\":1}";

    private static byte[] Key(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    private static async Task<AccountApiException> FailureOf(Func<Task> call) =>
        (await FluentActions.Awaiting(call).Should().ThrowAsync<AccountApiException>()).Which;

    [Fact]
    public async Task Each_route_round_trips()
    {
        await using var factory = new ServerFactory(postgres);
        var api = new HttpAccountApi(factory.CreateClient());
        var email = $"user{Guid.NewGuid():N}@exemplo.com";
        var salt = new byte[16];

        // Cadastro, confirmação e entrada.
        var register = new RegisterRequest(email, salt, Kdf, Key(1), Key(2), [1, 2, 3], [4, 5, 6]);
        await api.RegisterAsync(register);
        await api.ConfirmAsync(factory.Email.LastToken!, Key(1));
        var prelogin = await api.PreloginAsync(email);
        prelogin.KdfSalt.Should().Equal(salt);
        prelogin.KdfParams.Should().Be(Kdf);
        var login = await api.LoginAsync(email, Key(1), "pc");
        login.WrappedKeyPassword.Should().Equal(1, 2, 3);

        // Aparelhos.
        var second = await api.LoginAsync(email, Key(1), "celular");
        var devices = await api.ListDevicesAsync(login.Token);
        devices.Should().HaveCount(2);
        devices.Single(d => d.IsCurrent).Id.Should().Be(login.DeviceId);
        await api.RevokeDeviceAsync(login.Token, second.DeviceId);
        (await api.ListDevicesAsync(login.Token)).Should().ContainSingle();

        // Troca de senha.
        await api.ChangePasswordAsync(login.Token, new ChangePasswordRequest(Key(1), Key(3), salt, Kdf, [7, 7]));
        (await api.LoginAsync(email, Key(3), "pc")).WrappedKeyPassword.Should().Equal(7, 7);

        // Recuperação.
        (await api.RecoverBeginAsync(email, Key(2))).WrappedKeyRecovery.Should().Equal(4, 5, 6);
        await api.RecoverAsync(new RecoverRequest(email, Key(2), Key(4), salt, Kdf, [8, 8]));
        var recovered = await api.LoginAsync(email, Key(4), "pc");
        recovered.WrappedKeyPassword.Should().Equal(8, 8);

        // Sair e apagar a conta.
        await api.LogoutAsync(recovered.Token);
        var last = await api.LoginAsync(email, Key(4), "pc");
        await api.DeleteAccountAsync(last.Token, Key(4));
        (await FailureOf(() => api.LoginAsync(email, Key(4), "pc"))).Kind.Should().Be(AccountErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Errors_map_to_kinds()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
        {
            ["Limits:AuthPerEmailPerMinute"] = "6",
        });
        var account = await TestAccount.CreateAsync(factory);
        var api = new HttpAccountApi(factory.CreateClient());
        var token = account.First.Token;

        (await FailureOf(() => api.ListDevicesAsync("token-errado"))).Kind.Should().Be(AccountErrorKind.Unauthorized);
        (await FailureOf(() => api.ConfirmAsync("codigo-errado", Key(9)))).Kind.Should().Be(AccountErrorKind.InvalidToken);
        (await FailureOf(() => api.ConfirmAsync("x", [1]))).Kind.Should().Be(AccountErrorKind.InvalidRequest);
        (await FailureOf(() => api.RevokeDeviceAsync(token, Guid.NewGuid()))).Kind.Should().Be(AccountErrorKind.NotFound);

        var big = new RegisterRequest("a@exemplo.com", new byte[16], new string('x', 70 * 1024), Key(1), Key(2), [1], [1]);
        (await FailureOf(() => api.RegisterAsync(big))).Kind.Should().Be(AccountErrorKind.TooLarge);

        // Cadastro e entrada da conta já gastaram 2 dos 6 pedidos; o resto passa do limite.
        AccountApiException? limited = null;
        for (var i = 0; i < 10 && limited is null; i++)
        {
            try
            {
                await api.PreloginAsync(account.Email);
            }
            catch (AccountApiException e)
            {
                limited = e;
            }
        }

        limited.Should().NotBeNull();
        limited!.Kind.Should().Be(AccountErrorKind.RateLimited);
        limited.RetryAfter.Should().NotBeNull();
        limited.RetryAfter!.Value.TotalSeconds.Should().BeInRange(1, 60);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, "{\"error\":\"x\"}", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.OK, "isto não é json", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.OK, "", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.OK, "null", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.OK, "[null]", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.OK, "{\"kdf_salt\":null,\"kdf_params\":null}", AccountErrorKind.Unavailable)]
    [InlineData(HttpStatusCode.Gone, "", AccountErrorKind.InvalidRequest)]
    [InlineData(HttpStatusCode.BadRequest, "isto não é json", AccountErrorKind.InvalidRequest)]
    public async Task Unreadable_body_and_server_error_are_unavailable(HttpStatusCode status, string body, AccountErrorKind expected)
    {
        var api = new HttpAccountApi(new HttpClient(new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }))
        {
            BaseAddress = new Uri("http://localhost/"),
        });

        (await FailureOf(() => api.PreloginAsync("a@exemplo.com"))).Kind.Should().Be(expected);
        if (body is "[null]")
        {
            (await FailureOf(() => api.ListDevicesAsync("t"))).Kind.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Network_failure_is_unavailable()
    {
        var down = new HttpAccountApi(new HttpClient(new StubHandler(_ => throw new HttpRequestException("sem rede")))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        var timeout = new HttpAccountApi(new HttpClient(new StubHandler(_ => throw new TaskCanceledException("tempo esgotado")))
        {
            BaseAddress = new Uri("http://localhost/"),
        });

        (await FailureOf(() => down.LogoutAsync("t"))).Kind.Should().Be(AccountErrorKind.Unavailable);
        (await FailureOf(() => timeout.LogoutAsync("t"))).Kind.Should().Be(AccountErrorKind.Unavailable);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var api = new HttpAccountApi(new HttpClient(new StubHandler(_ => throw new TaskCanceledException()))
        {
            BaseAddress = new Uri("http://localhost/"),
        });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => api.LogoutAsync("t", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Bearer_token_is_sent_only_with_a_device_token()
    {
        var seen = new List<string?>();
        var api = new HttpAccountApi(new HttpClient(new StubHandler(r =>
        {
            seen.Add(r.Headers.Authorization?.ToString());
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }))
        {
            BaseAddress = new Uri("http://localhost/"),
        });

        await api.LogoutAsync("abc");
        await api.ConfirmAsync("x", Key(1));

        seen.Should().Equal("Bearer abc", null);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
