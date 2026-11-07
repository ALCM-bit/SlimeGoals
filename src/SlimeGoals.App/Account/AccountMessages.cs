using System.Globalization;
using System.Security.Cryptography;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Sync.Account;
using SlimeGoals.Sync.Crypto;

namespace SlimeGoals.App.Account;

/// <summary>A operação de conta em que o erro aconteceu: o mesmo erro pede textos diferentes.</summary>
public enum AccountAction
{
    Register,
    Confirm,
    Login,
    Recover,
    Reauthenticate,
    ChangePassword,
    SignOut,
    DeleteAccount,
    Devices,
}

/// <summary>
/// Texto para a pessoa de cada erro de conta (decisão 17 da 9.5a). Só a tabela fixa chega à tela: mensagem crua do
/// framework ou do servidor nunca passa.
/// </summary>
public static class AccountMessages
{
    public const string SessionEnded = "A sessão deste aparelho foi encerrada. Entre de novo.";
    public const string Generic = "Algo deu errado. Tente de novo.";
    public const string PasswordsDiffer = "As senhas não são iguais.";

    public static string For(Exception e, AccountAction action) => e switch
    {
        AccountApiException api => ForApi(api, action),
        KdfParamsException => "O servidor pediu parâmetros de segurança fora do aceito. Nada foi enviado.",
        SecretVaultUnavailableException =>
            "O chaveiro do sistema não está disponível ou o pedido foi recusado. Sem ele, a sincronização não liga.",
        FormatException when action == AccountAction.Recover => "Código de recuperação inválido. Confira os 26 caracteres.",
        CryptographicException when action == AccountAction.Recover => "Este código não abre a conta. Confira se é o desta conta.",
        ArgumentException or InvalidOperationException when ThrownBySlimeGoals(e) => e.Message,
        _ => Generic,
    };

    private static string ForApi(AccountApiException e, AccountAction action) => e.Kind switch
    {
        AccountErrorKind.Unauthorized => action switch
        {
            AccountAction.Login or AccountAction.Reauthenticate => "E-mail ou senha incorretos.",
            AccountAction.ChangePassword or AccountAction.DeleteAccount => "Senha incorreta.",
            AccountAction.Confirm => "Código inválido ou vencido.",
            AccountAction.Recover => "E-mail ou código de recuperação incorretos.",
            // Nas rotas com token (sair, aparelhos), o 401 é o token morto.
            AccountAction.SignOut or AccountAction.Devices => SessionEnded,
            _ => Generic,
        },
        AccountErrorKind.InvalidToken => SessionEnded,
        AccountErrorKind.RateLimited => e.RetryAfter is { } wait
            ? $"Muitas tentativas. Tente de novo em {Seconds(wait)} s."
            : "Muitas tentativas. Tente de novo daqui a um minuto.",
        AccountErrorKind.Unavailable => "Não foi possível falar com o servidor. Confira o endereço e a conexão.",
        AccountErrorKind.InvalidRequest or AccountErrorKind.TooLarge => "O servidor recusou os dados. Confira os campos.",
        AccountErrorKind.NotFound => "Não encontrado no servidor. Atualize a lista.",
        _ => Generic,
    };

    private static string Seconds(TimeSpan wait) =>
        Math.Max(1, (long)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// As mensagens de <see cref="ArgumentException"/> e <see cref="InvalidOperationException"/> lançadas pelo próprio
    /// código do SlimeGoals já são textos em pt-BR para a pessoa; as do framework, não.
    /// </summary>
    private static bool ThrownBySlimeGoals(Exception e) =>
        e.TargetSite?.DeclaringType?.Assembly.GetName().Name?.StartsWith("SlimeGoals", StringComparison.Ordinal) == true;
}
