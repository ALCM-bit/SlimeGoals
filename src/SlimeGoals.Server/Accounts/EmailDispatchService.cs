using Microsoft.Extensions.Options;
using SlimeGoals.Server.Limits;

namespace SlimeGoals.Server.Accounts;

/// <summary>
/// Envia os e-mails da <see cref="ConfirmationEmailQueue"/>, um por vez, em até 3 tentativas com espera crescente
/// (<c>Email:RetryDelaySeconds</c>, pelo <see cref="TimeProvider"/>). Se as 3 falharem, apaga a conta pendente:
/// nunca fica conta sem e-mail. O log leva só o e-mail mascarado e o tipo do erro, nunca o código nem a mensagem
/// do erro (a resposta do servidor SMTP pode repetir o endereço).
/// </summary>
public sealed partial class EmailDispatchService(
    ConfirmationEmailQueue queue,
    IEmailSender sender,
    AccountStore accounts,
    IOptions<ServerOptions> options,
    TimeProvider time,
    ILogger<EmailDispatchService> logger) : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(email, stoppingToken);
            }
            catch (Exception exception) when (!IsShutdown(exception, stoppingToken))
            {
                // Em geral, o banco recusou apagar: a conta pendente vence sozinha em Retention:UnconfirmedAccountHours.
                LogFailed(RateLimits.MaskEmail(email.Email), exception.GetType().Name);
            }
            finally
            {
                queue.Complete();
            }
        }
    }

    private async Task DispatchAsync(ConfirmationEmail email, CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await sender.SendConfirmationAsync(email.Email, email.Token, stoppingToken);
                return;
            }
            catch (Exception exception) when (!IsShutdown(exception, stoppingToken))
            {
                if (attempt == MaxAttempts)
                {
                    await accounts.DeleteUnconfirmedAsync(email.TokenHash, CancellationToken.None);
                    LogGaveUp(RateLimits.MaskEmail(email.Email), MaxAttempts, exception.GetType().Name);
                    return;
                }

                LogRetrying(RateLimits.MaskEmail(email.Email), attempt, exception.GetType().Name);
            }

            await Task.Delay(DelayBefore(attempt + 1), time, stoppingToken);
        }
    }

    /// <summary>
    /// Só o cancelamento da parada sobe. Qualquer outro erro, mesmo depois de a parada começar, vai para o log
    /// mascarado: se escapasse, o host registraria a exceção inteira, e a mensagem do MailKit pode trazer o e-mail.
    /// </summary>
    private static bool IsShutdown(Exception exception, CancellationToken stoppingToken) =>
        exception is OperationCanceledException && stoppingToken.IsCancellationRequested;

    /// <summary>A espera antes da tentativa dada (a 2ª usa o primeiro valor); faltando valor, repete o último.</summary>
    private TimeSpan DelayBefore(int attempt)
    {
        var delays = options.Value.Email.RetryDelaySeconds;
        if (delays.Length == 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Math.Max(0, delays[Math.Min(attempt - 2, delays.Length - 1)]));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Envio da confirmação para {MaskedEmail} falhou na tentativa {Attempt} ({Error}); tenta de novo.")]
    private partial void LogRetrying(string maskedEmail, int attempt, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Envio da confirmação para {MaskedEmail} falhou {Attempts} vezes ({Error}); o cadastro pendente foi apagado.")]
    private partial void LogGaveUp(string maskedEmail, int attempts, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "O envio da confirmação para {MaskedEmail} parou com erro ({Error}); o cadastro pendente vence sozinho.")]
    private partial void LogFailed(string maskedEmail, string error);
}
