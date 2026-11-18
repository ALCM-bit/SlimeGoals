using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class InviteTests(PostgresFixture postgres)
{
    private const string Invite = "código-secreto";

    private static Dictionary<string, string?> WithInvite() => new() { ["Server:InviteCode"] = Invite };

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string? invite) =>
        client.PostAsJsonAsync(
            "/auth/register",
            TestAccount.NewRegisterRequest("ana@exemplo.com") with { InviteCode = invite },
            SyncJsonContext.Default.RegisterRequest);

    private static async Task AssertClosedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("registration_closed");
    }

    [Fact]
    public async Task Register_without_invite_is_closed_when_invite_configured()
    {
        await using var factory = new ServerFactory(postgres, WithInvite());
        using var client = factory.CreateClient();

        await AssertClosedAsync(await RegisterAsync(client, null));
        factory.Email.Count.Should().Be(0);
    }

    [Fact]
    public async Task Register_with_wrong_invite_is_closed()
    {
        await using var factory = new ServerFactory(postgres, WithInvite());
        using var client = factory.CreateClient();

        await AssertClosedAsync(await RegisterAsync(client, "errado"));
        await AssertClosedAsync(await RegisterAsync(client, Invite + "x"));
        factory.Email.Count.Should().Be(0);
    }

    [Fact]
    public async Task Register_with_invite_padded_by_whitespace_is_accepted()
    {
        await using var factory = new ServerFactory(postgres, WithInvite());
        using var client = factory.CreateClient();

        (await RegisterAsync(client, $" {Invite}\n")).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Register_invite_is_case_sensitive()
    {
        await using var factory = new ServerFactory(postgres, WithInvite());
        using var client = factory.CreateClient();

        await AssertClosedAsync(await RegisterAsync(client, Invite.ToUpperInvariant()));
    }

    [Fact]
    public async Task Register_with_null_body_is_closed_when_invite_configured()
    {
        await using var factory = new ServerFactory(postgres, WithInvite());
        using var client = factory.CreateClient();

        await AssertClosedAsync(await client.PostAsync("/auth/register", new StringContent("null", System.Text.Encoding.UTF8, "application/json")));
    }

    [Fact]
    public async Task Production_without_invite_closes_registration()
    {
        await using var factory = ServerFactory.Production(postgres);
        using var client = factory.CreateClient();

        await AssertClosedAsync(await RegisterAsync(client, null));
        await AssertClosedAsync(await RegisterAsync(client, "qualquer"));
    }

    [Fact]
    public async Task Development_without_invite_keeps_registration_open()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        (await RegisterAsync(client, null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Default_quota_is_100000()
    {
        await using var factory = new ServerFactory(postgres);

        factory.Services.GetRequiredService<IOptions<ServerOptions>>().Value.Sync.MaxRecordsPerAccount.Should().Be(100000);
    }
}
