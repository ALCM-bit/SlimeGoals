using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Accounts;

/// <summary>Envia o e-mail de confirmação do cadastro.</summary>
public interface IEmailSender
{
    Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken);
}

/// <summary>Recusa a partida fora de Development enquanto só existir o envio simulado (decisão 8).</summary>
public sealed class EmailSenderValidator(IHostEnvironment environment, IEmailSender sender) : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        if (!environment.IsDevelopment() && sender is LogEmailSender)
        {
            return ValidateOptionsResult.Fail("Fora de Development é preciso registrar um IEmailSender real; LogEmailSender só existe em Development.");
        }

        return ValidateOptionsResult.Success;
    }
}
