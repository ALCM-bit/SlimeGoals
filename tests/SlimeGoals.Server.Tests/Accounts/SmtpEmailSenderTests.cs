using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Options;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed partial class SmtpEmailSenderTests(PostgresFixture postgres)
{
    // Fixada por digest (docker image inspect axllent/mailpit:v1.34.0, em 17/11/2026).
    private const string MailpitImage = "axllent/mailpit:v1.34.0@sha256:23a2ef85bc50853ab3093645d1d64679afd8887e89da5e48a88940c172591a4e";
    private const ushort SmtpPort = 1025;
    private const ushort ApiPort = 8025;

    [Fact]
    public async Task Smtp_sender_delivers_code_through_mailpit()
    {
        await using var mailpit = new ContainerBuilder(MailpitImage)
            .WithPortBinding(SmtpPort, true)
            .WithPortBinding(ApiPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/readyz")))
            .Build();
        await mailpit.StartAsync();
        await using var factory = ServerFactory.WithConfiguredEmail(postgres, new Dictionary<string, string?>
        {
            ["Email:Mode"] = "smtp",
            ["Email:Smtp:Host"] = mailpit.Hostname,
            ["Email:Smtp:Port"] = mailpit.GetMappedPublicPort(SmtpPort).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Email:Smtp:From"] = "SlimeGoals <nao-responda@exemplo.com>",
            ["Email:Smtp:Security"] = "None",
        });
        using var client = factory.CreateClient();
        var request = TestAccount.NewRegisterRequest("ana@exemplo.com");

        (await client.PostAsJsonAsync("/auth/register", request, SyncJsonContext.Default.RegisterRequest)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await factory.WaitForEmailQueueAsync();

        using var api = new HttpClient { BaseAddress = new Uri($"http://{mailpit.Hostname}:{mailpit.GetMappedPublicPort(ApiPort)}") };
        using var list = JsonDocument.Parse(await api.GetStringAsync("/api/v1/messages"));
        var message = list.RootElement.GetProperty("messages").EnumerateArray().Should().ContainSingle().Subject;
        message.GetProperty("Subject").GetString().Should().Be("Código de confirmação do SlimeGoals");
        message.GetProperty("To")[0].GetProperty("Address").GetString().Should().Be("ana@exemplo.com");
        using var full = JsonDocument.Parse(await api.GetStringAsync($"/api/v1/message/{message.GetProperty("ID").GetString()}"));
        var text = full.RootElement.GetProperty("Text").GetString()!;
        text.Should().Contain("vale por 24 horas").And.Contain("Se não foi você, ignore este e-mail.");
        var code = TokenPattern().Match(text);
        code.Success.Should().BeTrue();

        // O código do corpo é o que confirma a conta.
        (await client.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(code.Value, request.AuthKey), SyncJsonContext.Default.ConfirmRequest))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Production_rejects_log_mode()
    {
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?> { ["Email:Mode"] = "log" });

        ShouldRefuse(factory).Should().Contain("Email:Mode");
    }

    [Fact]
    public async Task Production_rejects_smtp_without_tls()
    {
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?> { ["Email:Smtp:Security"] = "None" });

        ShouldRefuse(factory).Should().Contain("Email:Smtp:Security");
    }

    [Fact]
    public async Task Production_rejects_username_without_password()
    {
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?> { ["Email:Smtp:Username"] = "usuario-smtp" });

        ShouldRefuse(factory).Should().Contain("Email:Smtp:Password").And.NotContain("usuario-smtp");
    }

    [Theory]
    [InlineData("Email:Smtp:Host")]
    [InlineData("Email:Smtp:From")]
    public async Task Production_rejects_smtp_without_host_or_from(string key)
    {
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?> { [key] = "" });

        ShouldRefuse(factory).Should().Contain("Email:Smtp:Host");
    }

    [Fact]
    public async Task Production_accepts_smtp_with_tls_and_credentials()
    {
        await using var factory = ServerFactory.Production(postgres, new Dictionary<string, string?>
        {
            ["Email:Smtp:Security"] = "SslOnConnect",
            ["Email:Smtp:Username"] = "usuario-smtp",
            ["Email:Smtp:Password"] = "senha-smtp",
        });

        (await factory.CreateClient().GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string ShouldRefuse(ServerFactory factory)
    {
        var act = () => factory.CreateClient();
        return act.Should().Throw<OptionsValidationException>().Which.Message;
    }

    [GeneratedRegex("(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])")]
    private static partial Regex TokenPattern();
}
