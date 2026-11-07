using System.Security.Cryptography;
using SlimeGoals.App.Account;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.App.Tests.Account;

public class AccountMessagesTests
{
    private const string SessionEnded = "A sessão deste aparelho foi encerrada. Entre de novo.";
    private const string Generic = "Algo deu errado. Tente de novo.";

    private static AccountApiException Api(AccountErrorKind kind, TimeSpan? retryAfter = null) => new(kind, retryAfter);

    public static TheoryData<string, AccountAction, string> Rows() =>
        new()
        {
            { "unauthorized", AccountAction.Login, "E-mail ou senha incorretos." },
            { "unauthorized", AccountAction.Reauthenticate, "E-mail ou senha incorretos." },
            { "unauthorized", AccountAction.ChangePassword, "Senha incorreta." },
            { "unauthorized", AccountAction.DeleteAccount, "Senha incorreta." },
            { "unauthorized", AccountAction.Confirm, "Código inválido ou vencido." },
            { "unauthorized", AccountAction.Recover, "E-mail ou código de recuperação incorretos." },
            { "unauthorized", AccountAction.SignOut, SessionEnded },
            { "unauthorized", AccountAction.Devices, SessionEnded },
            { "unauthorized", AccountAction.Register, Generic },
            { "invalid-token", AccountAction.Devices, SessionEnded },
            { "rate-limited-2.1", AccountAction.Login, "Muitas tentativas. Tente de novo em 3 s." },
            { "rate-limited", AccountAction.Login, "Muitas tentativas. Tente de novo daqui a um minuto." },
            { "unavailable", AccountAction.Confirm, "Não foi possível falar com o servidor. Confira o endereço e a conexão." },
            { "invalid-request", AccountAction.Register, "O servidor recusou os dados. Confira os campos." },
            { "too-large", AccountAction.Register, "O servidor recusou os dados. Confira os campos." },
            { "not-found", AccountAction.Devices, "Não encontrado no servidor. Atualize a lista." },
            { "kdf", AccountAction.Login, "O servidor pediu parâmetros de segurança fora do aceito. Nada foi enviado." },
            { "vault", AccountAction.Login,
                "O chaveiro do sistema não está disponível ou o pedido foi recusado. Sem ele, a sincronização não liga." },
            { "format", AccountAction.Recover, "Código de recuperação inválido. Confira os 26 caracteres." },
            { "format", AccountAction.Login, Generic },
            { "crypto", AccountAction.Recover, "Este código não abre a conta. Confira se é o desta conta." },
            { "crypto", AccountAction.Login, Generic },
            { "other", AccountAction.Login, Generic },
        };

    private static Exception Of(string name) => name switch
    {
        "unauthorized" => Api(AccountErrorKind.Unauthorized),
        "invalid-token" => Api(AccountErrorKind.InvalidToken),
        "rate-limited-2.1" => Api(AccountErrorKind.RateLimited, TimeSpan.FromSeconds(2.1)),
        "rate-limited" => Api(AccountErrorKind.RateLimited),
        "unavailable" => Api(AccountErrorKind.Unavailable),
        "invalid-request" => Api(AccountErrorKind.InvalidRequest),
        "too-large" => Api(AccountErrorKind.TooLarge),
        "not-found" => Api(AccountErrorKind.NotFound),
        "kdf" => new KdfParamsException("Campo inválido nos parâmetros de derivação: m."),
        "vault" => new SecretVaultUnavailableException("O cofre de segredos do sistema não está disponível."),
        "format" => new FormatException("O código de recuperação deve ter 26 caracteres."),
        "crypto" => new CryptographicException("tag mismatch"),
        _ => new IOException("disco cheio em /home/ana"),
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Each_error_has_its_text(string error, AccountAction action, string text) =>
        AccountMessages.For(Of(error), action).Should().Be(text);

    [Fact]
    public void Own_argument_and_invalid_operation_messages_are_shown()
    {
        AccountMessages.For(Thrown(() => throw new InvalidOperationException("Saia da conta antes de entrar em outra.")),
            AccountAction.Login).Should().Be("Saia da conta antes de entrar em outra.");
        // Como em AccountService: com paramName, que o .NET acrescenta à Message.
        AccountMessages.For(Thrown(() => throw new ArgumentException("Informe um e-mail válido, com até 254 caracteres.", "email")),
            AccountAction.Register).Should().Be("Informe um e-mail válido, com até 254 caracteres.");
        AccountMessages.For(Thrown(() => throw new ArgumentException("O nome do aparelho deve ter de 1 a 100 caracteres.",
            "deviceName")), AccountAction.Login).Should().Be("O nome do aparelho deve ter de 1 a 100 caracteres.");
        AccountMessages.For(Thrown(() => throw new ArgumentException("Este cadastro é de outro servidor.")),
            AccountAction.Confirm).Should().Be("Este cadastro é de outro servidor.");
    }

    [Fact]
    public void Own_argument_out_of_range_is_generic() =>
        AccountMessages.For(Thrown(() => throw new ArgumentOutOfRangeException("key")), AccountAction.Login)
            .Should().Be(Generic);

    [Fact]
    public void Framework_argument_and_invalid_operation_messages_are_hidden()
    {
        AccountMessages.For(Thrown(() => ArgumentNullException.ThrowIfNull((object?)null, "password")), AccountAction.Login)
            .Should().Be(Generic);
        AccountMessages.For(Thrown(() => _ = new List<int>().First()), AccountAction.Devices).Should().Be(Generic);
        AccountMessages.For(new InvalidOperationException("nunca lançada"), AccountAction.Login).Should().Be(Generic);
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("A ação não lançou.");
    }
}
