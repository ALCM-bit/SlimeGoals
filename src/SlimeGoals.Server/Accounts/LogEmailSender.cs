namespace SlimeGoals.Server.Accounts;

/// <summary>
/// E-mail simulado: escreve o token no log para quem desenvolve confirmar a conta. É a única exceção
/// à regra de logs da spec (§5, regra 8, prevista na §10) e só roda em Development.
/// </summary>
public sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    public Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
    {
        LogConfirmation(logger, email, token);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-mail simulado para {Email}: token de confirmação {Token}")]
    private static partial void LogConfirmation(ILogger logger, string email, string token);
}
