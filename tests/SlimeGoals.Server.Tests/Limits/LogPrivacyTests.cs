using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Limits;

public sealed class LogPrivacyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Logs_never_contain_email_token_or_blob()
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Trace",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            ["Logging:LogLevel:Microsoft"] = "Trace",
            ["Logging:LogLevel:System"] = "Trace",
            ["Logging:LogLevel:Npgsql"] = "Trace",
            ["Limits:AuthPerEmailPerMinute"] = "4",
            ["Limits:MaxBlobBytes"] = "65536",
            ["Limits:AuthKeyFailuresPerAccountPerMinute"] = "1",
        });
        var email = $"pessoa{Guid.NewGuid():N}@exemplo.com";
        var registerRequest = TestAccount.NewRegisterRequest(email);
        var blob = RandomNumberGenerator.GetBytes(48);
        var secrets = new List<string> { email, email.ToUpperInvariant() };

        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/auth/register", registerRequest, SyncJsonContext.Default.RegisterRequest)).EnsureSuccessStatusCode();
        var confirmToken = factory.Email.LastToken;
        secrets.Add(confirmToken);
        (await anonymous.PostAsJsonAsync("/auth/confirm", new ConfirmRequest(confirmToken), SyncJsonContext.Default.ConfirmRequest))
            .EnsureSuccessStatusCode();

        var login = await anonymous.PostAsJsonAsync(
            "/auth/login", new LoginRequest(email, registerRequest.AuthKey, "pc"), SyncJsonContext.Default.LoginRequest);
        var session = (await login.Content.ReadFromJsonAsync(SyncJsonContext.Default.LoginResponse))!;
        secrets.Add(session.Token);
        secrets.Add(Convert.ToBase64String(registerRequest.AuthKey!));
        secrets.Add(Convert.ToBase64String(registerRequest.RecoveryKey!));
        secrets.Add(Convert.ToBase64String(blob));

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.Token);
        (await client.PostAsJsonAsync(
            "/sync/push", new PushRequest([new PushItemDto("k", 0, blob, false)]), SyncJsonContext.Default.PushRequest)).EnsureSuccessStatusCode();
        (await client.GetAsync("/sync/pull")).EnsureSuccessStatusCode();

        // Erros: chave errada, corpo grande, JSON ruim, token ruim e os limites estourados.
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(RandomNumberGenerator.GetBytes(32)), SyncJsonContext.Default.DeleteAccountRequest),
        });
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest(registerRequest.AuthKey), SyncJsonContext.Default.DeleteAccountRequest),
        });
        await client.PostAsync("/sync/push", new StringContent("{ nao e json " + email, Encoding.UTF8, "application/json"));
        await anonymous.PostAsync("/auth/login", new StringContent(new string('x', 70 * 1024), Encoding.UTF8, "application/json"));
        using var bad = factory.CreateClient();
        bad.DefaultRequestHeaders.Authorization = new("Bearer", "token-desconhecido");
        await bad.GetAsync("/sync/pull");
        for (var i = 0; i < 6; i++)
        {
            await anonymous.PostAsJsonAsync("/auth/prelogin", new PreloginRequest(email), SyncJsonContext.Default.PreloginRequest);
        }

        var lines = factory.Logs.Lines;
        lines.Should().NotBeEmpty();
        foreach (var secret in secrets)
        {
            lines.Where(l => l.Contains(secret, StringComparison.OrdinalIgnoreCase)).Should().BeEmpty($"o log não pode conter {secret[..6]}...");
        }
    }
}
