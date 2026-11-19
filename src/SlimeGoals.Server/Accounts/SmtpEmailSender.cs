using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace SlimeGoals.Server.Accounts;

/// <summary>
/// Envia o código de confirmação por SMTP (MailKit). A proteção da conexão é a de <c>Email:Smtp:Security</c>, sem
/// <c>Auto</c>, e o certificado do servidor é sempre conferido. Cada envio abre e fecha a sua conexão.
/// </summary>
public sealed class SmtpEmailSender(IOptions<ServerOptions> options, TimeProvider time) : IEmailSender
{
    public const string Subject = "Código de confirmação do SlimeGoals";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendConfirmationAsync(string email, string token, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Email.Smtp;
        using var message = new MimeMessage
        {
            Subject = Subject,
            Date = time.GetUtcNow(),
            Body = new TextPart(TextFormat.Plain) { Text = Body(token, options.Value.Retention.UnconfirmedAccountHours) },
        };
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(new MailboxAddress("", email));

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        await client.ConnectAsync(smtp.Host, smtp.Port, SocketOptions(smtp.Security), cancellationToken);
        if (!string.IsNullOrWhiteSpace(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    private static SecureSocketOptions SocketOptions(ServerOptions.SmtpSecurity security) => security switch
    {
        ServerOptions.SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        ServerOptions.SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        ServerOptions.SmtpSecurity.None => SecureSocketOptions.None,
        _ => throw new InvalidOperationException("Email:Smtp:Security inválido."),
    };

    private static string Body(string token, int hours) => $"""
        Seu código de confirmação do SlimeGoals:

        {token}

        Digite-o no app para concluir o cadastro. O código vale por {hours} horas.

        Se não foi você, ignore este e-mail.
        """;
}
