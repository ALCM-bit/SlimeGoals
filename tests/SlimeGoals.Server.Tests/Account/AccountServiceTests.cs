using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Domain;
using SlimeGoals.Core.Sync;
using SlimeGoals.Infrastructure.Data;
using SlimeGoals.Infrastructure.Tests;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Contracts;
using SlimeGoals.Sync.Crypto;
using SlimeGoals.Testing;

namespace SlimeGoals.Server.Tests.Account;

public sealed class AccountServiceTests(PostgresFixture postgres)
{
    private const string Password = "senha-forte-ação-123";
    private const string NewPassword = "outra-senha-bem-longa";
    private const string SessionName = "sync-session";

    private static string NewEmail() => $"user{Guid.NewGuid():N}@exemplo.com";

    private static async Task<AccountApiException> FailureOf(Func<Task> call) =>
        (await FluentActions.Awaiting(call).Should().ThrowAsync<AccountApiException>()).Which;

    /// <summary>Cadastra e confirma pelo serviço; devolve o código de recuperação.</summary>
    private static async Task<string> RegisterAndConfirmAsync(ServerFactory factory, Device device, string email, string name = "pc")
    {
        using var pending = await device.Service.RegisterAsync(email, Password);
        await device.Service.ConfirmAsync(pending, factory.Email.LastToken, name);
        return pending.RecoveryCode;
    }

    private static SyncEnvelope Envelope() =>
        new(SyncCodec.FormatVersion, SyncRecordKind.Goal, Guid.NewGuid(), Guid.NewGuid(), "{\"title\":\"Ler\"}");

    /// <summary>Selado por um aparelho, aberto pelo outro: os dois têm a mesma chave de dados.</summary>
    private static async Task ShouldShareDataKeyAsync(Device sealer, Device opener)
    {
        using var a = (await sealer.Service.OpenSessionAsync())!;
        using var b = (await opener.Service.OpenSessionAsync())!;
        var envelope = Envelope();
        var blob = a.Crypto.Seal(envelope);
        b.Crypto.Open(b.Crypto.RecordKey(envelope.RecordId), blob).Should().Be(envelope);
    }

    [Fact]
    public async Task Register_confirm_and_login_store_the_session_and_enable_sync()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        var email = NewEmail();

        using (var pending = await device.Service.RegisterAsync($"  {email.ToUpperInvariant()} ", Password))
        {
            pending.Email.Should().Be(email);
            pending.RecoveryCode.Should().MatchRegex("^([0-9A-HJKMNP-TV-Z]{4}-){6}[0-9A-HJKMNP-TV-Z]{2}$");
            device.Vault.Names.Should().BeEmpty();
            (await device.State.GetAsync()).Enabled.Should().BeFalse();

            await device.Service.ConfirmAsync(pending, factory.Email.LastToken, "pc");
        }

        using var secret = await device.SecretAsync();
        var root = secret.RootElement;
        root.GetProperty("server").GetString().Should().Be(device.Server.ToString());
        root.GetProperty("email").GetString().Should().Be(email);
        var deviceId = root.GetProperty("device_id").GetGuid();
        root.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        Convert.FromBase64String(root.GetProperty("data_key").GetString()!).Should().HaveCount(32);
        (await device.State.GetAsync()).Enabled.Should().BeTrue();

        var devices = await device.Service.ListDevicesAsync();
        devices.Should().ContainSingle().Which.Should().Match<DeviceDto>(d => d.Id == deviceId && d.IsCurrent && d.Name == "pc");

        using var session = (await device.Service.OpenSessionAsync())!;
        session.DeviceId.Should().Be(deviceId);
        session.Email.Should().Be(email);
        session.Server.Should().Be(device.Server);
        (await session.Token(CancellationToken.None)).Should().Be(root.GetProperty("token").GetString());
    }

    [Fact]
    public async Task Register_sends_only_derived_material()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        var recoveryCode = await RegisterAndConfirmAsync(factory, device, NewEmail());

        using var secret = await device.SecretAsync();
        var dataKey = Convert.FromBase64String(secret.RootElement.GetProperty("data_key").GetString()!);
        var bodies = string.Join("\n", device.Handler.Sent.Select(s => s.Body));
        var forbidden = new[]
        {
            Password,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Password)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Password.Normalize(NormalizationForm.FormKC))),
            Convert.ToBase64String(dataKey),
            recoveryCode,
            recoveryCode.Replace("-", "", StringComparison.Ordinal),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(recoveryCode)),
        };
        foreach (var value in forbidden)
            bodies.Should().NotContain(value);

        using var register = JsonDocument.Parse(device.Handler.Sent.Single(s => s.Path == "/auth/register").Body);
        var r = register.RootElement;
        var authKey = r.GetProperty("auth_key").GetBytesFromBase64();
        var recoveryKey = r.GetProperty("recovery_key").GetBytesFromBase64();
        var wrappedPassword = r.GetProperty("wrapped_key_password").GetBytesFromBase64();
        var wrappedRecovery = r.GetProperty("wrapped_key_recovery").GetBytesFromBase64();
        r.GetProperty("kdf_salt").GetBytesFromBase64().Should().HaveCount(16).And.NotEqual(new byte[16]);
        r.GetProperty("kdf_params").GetString().Should().Be(KdfParams.Default.ToJson());

        // Chave zerada antes do uso seria uma chave pública: nada pode ser zero nem abrir com zeros.
        var zero = new byte[32];
        authKey.Should().NotEqual(zero);
        recoveryKey.Should().NotEqual(zero);
        FluentActions.Invoking(() => KeyWrap.Unwrap(wrappedPassword, zero, WrapPurpose.Password)).Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => KeyWrap.Unwrap(wrappedRecovery, zero, WrapPurpose.Recovery)).Should().Throw<CryptographicException>();
        using (var zeroCode = RecoveryCode.Parse(new string('0', 26)))
        using (var zeroKeys = zeroCode.Keys())
        {
            zeroKeys.RecoveryKey.Should().NotEqual(recoveryKey);
            FluentActions.Invoking(() => KeyWrap.Unwrap(wrappedRecovery, zeroKeys.WrapKey, WrapPurpose.Recovery))
                .Should().Throw<CryptographicException>();
        }

        // O código mostrado à pessoa abre o embrulho de recuperação e dá a chave de dados do cofre.
        using var code = RecoveryCode.Parse(recoveryCode);
        using var keys = code.Keys();
        keys.RecoveryKey.Should().Equal(recoveryKey);
        KeyWrap.Unwrap(wrappedRecovery, keys.WrapKey, WrapPurpose.Recovery).Should().Equal(dataKey);
    }

    [Fact]
    public async Task Second_device_logs_in_and_opens_the_same_data_key()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);

        await second.Service.LoginAsync(email, Password, "celular");

        (await second.State.GetAsync()).Enabled.Should().BeTrue();
        await ShouldShareDataKeyAsync(first, second);
        (await second.Service.ListDevicesAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized_and_stores_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);

        (await FailureOf(() => second.Service.LoginAsync(email, "senha-errada-123", "celular"))).Kind
            .Should().Be(AccountErrorKind.Unauthorized);

        second.Vault.Names.Should().BeEmpty();
        (await second.State.GetAsync()).Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"v\":1,\"m\":1024,\"t\":1,\"p\":1}")]
    [InlineData("{\"v\":1,\"m\":4194304,\"t\":3,\"p\":1}")]
    [InlineData("{\"v\":2,\"m\":65536,\"t\":3,\"p\":1}")]
    [InlineData("isto não é json")]
    public async Task Login_refuses_weak_params_from_the_server_before_sending_anything(string kdfParams)
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        device.Handler.Intercept = request => request.RequestUri!.AbsolutePath != "/auth/prelogin"
            ? null
            : Json(JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["kdf_salt"] = Convert.ToBase64String(new byte[16]),
                ["kdf_params"] = kdfParams,
            }));

        await FluentActions.Awaiting(() => device.Service.LoginAsync(NewEmail(), Password, "pc"))
            .Should().ThrowAsync<KdfParamsException>();

        device.Handler.Sent.Select(s => s.Path).Should().Equal("/auth/prelogin");
        device.Vault.Names.Should().BeEmpty();
    }

    [Fact]
    public async Task Login_refuses_a_short_salt_from_the_server_before_sending_anything()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        device.Handler.Intercept = request => request.RequestUri!.AbsolutePath != "/auth/prelogin"
            ? null
            : Json($$"""{"kdf_salt":"{{Convert.ToBase64String(new byte[4])}}","kdf_params":{{JsonSerializer.Serialize(KdfParams.Default.ToJson())}}}""");

        await FluentActions.Awaiting(() => device.Service.LoginAsync(NewEmail(), Password, "pc"))
            .Should().ThrowAsync<KdfParamsException>();

        device.Handler.Sent.Select(s => s.Path).Should().Equal("/auth/prelogin");
    }

    [Fact]
    public async Task Vault_unavailable_stops_before_any_request()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        device.Vault.Available = false;

        await FluentActions.Awaiting(() => device.Service.RegisterAsync(NewEmail(), Password))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        await FluentActions.Awaiting(() => device.Service.LoginAsync(NewEmail(), Password, "pc"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();
        await FluentActions.Awaiting(() => device.Service.RecoverAsync(NewEmail(), new string('0', 26), NewPassword, "pc"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();

        device.Handler.Sent.Should().BeEmpty();
        (await device.State.GetAsync()).Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Vault_failure_after_login_logs_out_and_enables_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);
        second.Vault.FailWritesWith = new SecretVaultUnavailableException("desbloqueio recusado");

        await FluentActions.Awaiting(() => second.Service.LoginAsync(email, Password, "celular"))
            .Should().ThrowAsync<SecretVaultUnavailableException>().WithMessage("desbloqueio recusado");

        second.Vault.FailWritesWith = null;
        second.Vault.Names.Should().BeEmpty();
        (await second.State.GetAsync()).Enabled.Should().BeFalse();
        second.Handler.Sent.Select(s => s.Path).Should().Equal("/auth/prelogin", "/auth/login", "/auth/logout");
        var issued = second.Handler.LoginTokens.Should().ContainSingle().Which;
        var raw = new HttpAccountApi(factory.CreateClient());
        (await FailureOf(() => raw.ListDevicesAsync(issued))).Kind.Should().Be(AccountErrorKind.Unauthorized);
        (await first.Service.ListDevicesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Vault_failure_after_confirm_logs_out_and_enables_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        using var pending = await device.Service.RegisterAsync(NewEmail(), Password);
        device.Vault.FailWritesWith = new SecretVaultUnavailableException("desbloqueio recusado");

        await FluentActions.Awaiting(() => device.Service.ConfirmAsync(pending, factory.Email.LastToken, "pc"))
            .Should().ThrowAsync<SecretVaultUnavailableException>();

        device.Vault.Names.Should().BeEmpty();
        (await device.State.GetAsync()).Enabled.Should().BeFalse();
        device.Handler.Sent.Select(s => s.Path).Should().EndWith(["/auth/login", "/auth/logout"]);
        var raw = new HttpAccountApi(factory.CreateClient());
        (await FailureOf(() => raw.ListDevicesAsync(device.Handler.LoginTokens.Single()))).Kind.Should().Be(AccountErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Login_while_enabled_is_refused()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        var email = NewEmail();
        var code = await RegisterAndConfirmAsync(factory, device, email);
        var sent = device.Handler.Sent.Count;
        const string message = "Saia da conta antes de entrar em outra.";

        await FluentActions.Awaiting(() => device.Service.LoginAsync(email, Password, "pc"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await FluentActions.Awaiting(() => device.Service.RegisterAsync(NewEmail(), Password))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await FluentActions.Awaiting(() => device.Service.RecoverAsync(email, code, NewPassword, "pc"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);

        device.Handler.Sent.Should().HaveCount(sent);
    }

    [Fact]
    public async Task Change_password_keeps_this_device_and_revokes_the_others()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        using var third = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);
        await second.Service.LoginAsync(email, Password, "celular");
        var vaultBefore = await first.Vault.GetAsync(SessionName);

        await first.Service.ChangePasswordAsync(Password, NewPassword);

        (await first.Vault.GetAsync(SessionName)).Should().Equal(vaultBefore);
        (await first.Service.ListDevicesAsync()).Should().ContainSingle().Which.IsCurrent.Should().BeTrue();
        (await FailureOf(() => second.Service.ListDevicesAsync())).Kind.Should().Be(AccountErrorKind.Unauthorized);

        (await FailureOf(() => third.Service.LoginAsync(email, Password, "novo"))).Kind.Should().Be(AccountErrorKind.Unauthorized);
        await third.Service.LoginAsync(email, NewPassword, "novo");
        await ShouldShareDataKeyAsync(first, third);
    }

    [Fact]
    public async Task Change_password_refuses_a_weak_new_password_before_any_request()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        await RegisterAndConfirmAsync(factory, device, NewEmail());
        var sent = device.Handler.Sent.Count;

        await FluentActions.Awaiting(() => device.Service.ChangePasswordAsync(Password, "curta"))
            .Should().ThrowAsync<ArgumentException>();

        device.Handler.Sent.Should().HaveCount(sent);
    }

    [Fact]
    public async Task Recover_on_a_new_device_opens_the_data_and_revokes_everything()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        using var fresh = new Device(factory);
        var email = NewEmail();
        var code = await RegisterAndConfirmAsync(factory, first, email);
        await second.Service.LoginAsync(email, Password, "celular");

        var typed = code.Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();
        (await fresh.Service.RecoverAsync(email, typed, NewPassword, "novo")).Should().Be(code);

        (await fresh.State.GetAsync()).Enabled.Should().BeTrue();
        (await fresh.Service.ListDevicesAsync()).Should().ContainSingle().Which.Name.Should().Be("novo");
        (await FailureOf(() => first.Service.ListDevicesAsync())).Kind.Should().Be(AccountErrorKind.Unauthorized);
        (await FailureOf(() => second.Service.ListDevicesAsync())).Kind.Should().Be(AccountErrorKind.Unauthorized);
        await ShouldShareDataKeyAsync(first, fresh);
        fresh.Handler.Sent.Select(s => s.Path).Should().NotContain("/auth/prelogin");
    }

    [Fact]
    public async Task Wrong_recovery_code_is_unauthorized()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var fresh = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);
        using var wrong = RecoveryCode.Generate();

        (await FailureOf(() => fresh.Service.RecoverAsync(email, wrong.Display, NewPassword, "novo"))).Kind
            .Should().Be(AccountErrorKind.Unauthorized);

        fresh.Vault.Names.Should().BeEmpty();
        (await fresh.State.GetAsync()).Enabled.Should().BeFalse();
        (await first.Service.ListDevicesAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Sign_out_clears_vault_and_state_even_offline()
    {
        await using var factory = new ServerFactory(postgres);
        using var first = new Device(factory);
        using var second = new Device(factory);
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, first, email);
        await second.Service.LoginAsync(email, Password, "celular");
        first.Handler.Intercept = request => request.RequestUri!.AbsolutePath == "/auth/logout"
            ? throw new HttpRequestException("sem rede")
            : null;

        await first.Service.SignOutAsync();

        first.Vault.Names.Should().BeEmpty();
        (await first.State.GetAsync()).Enabled.Should().BeFalse();
        (await first.Service.OpenSessionAsync()).Should().BeNull();

        // Com rede, o token deste aparelho também cai no servidor.
        var token = second.Handler.LoginTokens.Single();
        await second.Service.SignOutAsync();
        second.Vault.Names.Should().BeEmpty();
        (await second.State.GetAsync()).Enabled.Should().BeFalse();
        var raw = new HttpAccountApi(factory.CreateClient());
        (await FailureOf(() => raw.ListDevicesAsync(token))).Kind.Should().Be(AccountErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Delete_account_removes_the_cloud_and_keeps_local_data()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        using var other = new Device(factory);
        await new GoalRepository(device.Db.Factory).AddAsync(NewGoal());
        var email = NewEmail();
        await RegisterAndConfirmAsync(factory, device, email);

        await FluentActions.Awaiting(() => device.Service.DeleteAccountAsync("senha-errada-123"))
            .Should().ThrowAsync<AccountApiException>();
        device.Vault.Names.Should().ContainSingle();

        await device.Service.DeleteAccountAsync(Password);

        device.Vault.Names.Should().BeEmpty();
        (await device.State.GetAsync()).Enabled.Should().BeFalse();
        device.Handler.Sent.Select(s => s.Path).Should().NotContain("/auth/logout");
        device.Db.Anchor.ExecuteScalar<int>("SELECT COUNT(*) FROM goals").Should().Be(1);
        (await FailureOf(() => other.Service.LoginAsync(email, Password, "pc"))).Kind.Should().Be(AccountErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Open_session_returns_null_without_a_session()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);

        (await device.Service.OpenSessionAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Operations_that_need_a_session_ask_to_sign_in_first()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        const string message = "Entre na conta antes.";

        await FluentActions.Awaiting(() => device.Service.ChangePasswordAsync(Password, NewPassword))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await FluentActions.Awaiting(() => device.Service.ListDevicesAsync())
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await FluentActions.Awaiting(() => device.Service.RevokeDeviceAsync(Guid.NewGuid()))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
        await FluentActions.Awaiting(() => device.Service.DeleteAccountAsync(Password))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);

        device.Handler.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_arguments_are_refused_before_any_request()
    {
        await using var factory = new ServerFactory(postgres);
        using var device = new Device(factory);
        var longEmail = new string('a', 250) + "@x.com";

        foreach (var email in new[] { "", "   ", longEmail, "sem-arroba" })
        {
            await FluentActions.Awaiting(() => device.Service.RegisterAsync(email, Password)).Should().ThrowAsync<ArgumentException>();
            await FluentActions.Awaiting(() => device.Service.LoginAsync(email, Password, "pc")).Should().ThrowAsync<ArgumentException>();
        }

        await FluentActions.Awaiting(() => device.Service.RegisterAsync(NewEmail(), "curta")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => device.Service.LoginAsync(NewEmail(), Password, " ")).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => device.Service.LoginAsync(NewEmail(), Password, new string('x', 101)))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => device.Service.RecoverAsync(NewEmail(), new string('0', 26), "curta", "pc"))
            .Should().ThrowAsync<ArgumentException>();

        device.Handler.Sent.Should().BeEmpty();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Goal NewGoal() => new()
    {
        Id = Guid.CreateVersion7(),
        Title = "Ler",
        Recurrence = Recurrence.Daily,
        Target = 1,
        Icon = "book",
        Color = GoalColor.Pink,
        ReminderEveryMinutes = 90,
        ActiveFrom = new TimeOnly(9, 0),
        ActiveUntil = new TimeOnly(18, 0),
        ActiveDays = WeekDays.Monday,
        CreatedAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
    };

    /// <summary>Um aparelho: banco SQLite, cofre em memória e o serviço falando com o servidor por um gravador.</summary>
    private sealed class Device : IDisposable
    {
        public Device(ServerFactory factory)
        {
            var client = factory.CreateDefaultClient(Handler);
            Server = client.BaseAddress!;
            State = new SyncStateRepository(Db.Factory);
            Service = new AccountService(new HttpAccountApi(client), Vault, State, Server);
        }

        public TestDatabase Db { get; } = new();
        public InMemorySecretVault Vault { get; } = new();
        public Recorder Handler { get; } = new();
        public SyncStateRepository State { get; }
        public Uri Server { get; }
        public AccountService Service { get; }

        public async Task<JsonDocument> SecretAsync() =>
            JsonDocument.Parse((await Vault.GetAsync(SessionName)).Should().NotBeNull().And.Subject.ToArray());

        public void Dispose() => Db.Dispose();
    }

    private sealed record SentRequest(HttpMethod Method, string Path, string Body);

    /// <summary>Grava cada pedido e os tokens emitidos no <c>login</c>; <see cref="Intercept"/> responde no lugar do servidor.</summary>
    private sealed class Recorder : DelegatingHandler
    {
        private readonly List<SentRequest> _sent = [];
        private readonly List<string> _tokens = [];

        public Func<HttpRequestMessage, HttpResponseMessage?>? Intercept { get; set; }

        public IReadOnlyList<SentRequest> Sent
        {
            get { lock (_sent) return _sent.ToArray(); }
        }

        public IReadOnlyList<string> LoginTokens
        {
            get { lock (_tokens) return _tokens.ToArray(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            lock (_sent) _sent.Add(new SentRequest(request.Method, path, body));
            if (Intercept?.Invoke(request) is { } fake)
                return fake;

            var response = await base.SendAsync(request, cancellationToken);
            if (path == "/auth/login" && response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken);
                using (var doc = JsonDocument.Parse(text))
                    lock (_tokens) _tokens.Add(doc.RootElement.GetProperty("token").GetString()!);
                response.Content = new StringContent(text, Encoding.UTF8, "application/json");
            }

            return response;
        }
    }
}
