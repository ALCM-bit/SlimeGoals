namespace SlimeGoals.Sync.Account;

/// <summary>
/// A recuperação foi enviada, mas o aparelho não conseguiu saber se o servidor a aplicou: a resposta se perdeu e a
/// conferência também não chegou. Se aplicou, o código que vale é <see cref="NewRecoveryCode"/>; se não, o antigo
/// continua valendo. O código vai só na propriedade, nunca na mensagem.
/// </summary>
public sealed class RecoveryUnconfirmedException(string newRecoveryCode, Exception inner)
    : Exception("Não foi possível confirmar a recuperação com o servidor.", inner)
{
    public string NewRecoveryCode { get; } = newRecoveryCode;
}
