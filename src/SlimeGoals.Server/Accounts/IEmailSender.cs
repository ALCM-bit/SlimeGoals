using Microsoft.Extensions.Options;

namespace SlimeGoals.Server.Accounts;

/// <summary>Envia o e-mail de confirmação do cadastro.</summary>
public interface IEmailSender
{
    Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken);
}

/// <summary>
/// Recusa a partida fora de Development com o e-mail simulado, SMTP sem servidor ou remetente, SMTP sem TLS ou
/// usuário sem senha (D11). As mensagens são fixas: nunca repetem um valor da configuração.
/// </summary>
public sealed class EmailSenderValidator(IHostEnvironment environment) : IValidateOptions<ServerOptions>
{
    public ValidateOptionsResult Validate(string? name, ServerOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        var email = options.Email;
        if (email.Mode != ServerOptions.EmailMode.Smtp)
        {
            return ValidateOptionsResult.Fail("Email:Mode=log só vale em Development; fora dele use smtp.");
        }

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(email.Smtp.Host) || string.IsNullOrWhiteSpace(email.Smtp.From))
        {
            failures.Add("Com Email:Mode=smtp, Email:Smtp:Host e Email:Smtp:From são obrigatórios.");
        }

        if (email.Smtp.Security is not (ServerOptions.SmtpSecurity.StartTls or ServerOptions.SmtpSecurity.SslOnConnect))
        {
            failures.Add("Email:Smtp:Security precisa ser StartTls ou SslOnConnect fora de Development.");
        }

        if (!string.IsNullOrWhiteSpace(email.Smtp.Username) && string.IsNullOrWhiteSpace(email.Smtp.Password))
        {
            failures.Add("Email:Smtp:Username exige Email:Smtp:Password.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
