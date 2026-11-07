using SlimeGoals.Server.Limits;

namespace SlimeGoals.Server.Accounts;

/// <summary>
/// E-mail simulado: escreve o token no log para quem desenvolve confirmar a conta. É a única exceção
/// à regra de logs da spec (§5, regra 8, prevista na §10), vale só para o token e só roda em Development.
/// O e-mail sai mascarado, como em qualquer outro log.
/// </summary>
public sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
    {
        LogConfirmation(logger, AuthRateLimits.MaskEmail(email), token);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail simulado para {MaskedEmail}: token de confirmação {Token}")]
    private static partial void LogConfirmation(ILogger logger, string maskedEmail, string token);
}
