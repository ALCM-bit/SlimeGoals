using Microsoft.Extensions.Logging;
using SlimeGoals.Server.Accounts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class LogEmailSenderTests
{
    [Fact]
    public async Task Log_carries_the_token_but_only_the_masked_email()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var sender = new LogEmailSender(loggerFactory.CreateLogger<LogEmailSender>());
        const string email = "pessoa.secreta@exemplo.com";
        var token = SecretHash.NewToken();

        await sender.SendConfirmationAsync(email, token, CancellationToken.None);

        var line = logs.Lines.Should().ContainSingle().Which;
        line.Should().Contain(token);
        line.Should().Contain("p***@exemplo.com");
        line.Should().NotContain(email);
        line.Should().NotContain("pessoa.secreta");
    }
}
