using System.Net;
using System.Net.Http.Json;
using Dapper;
using Npgsql;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class DevicesTests(PostgresFixture postgres)
{
    private static async Task<DeviceDto[]> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync("/devices", SyncJsonContext.Default.DeviceDtoArray))!;

    [Fact]
    public async Task Devices_lists_only_this_account()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");
        var other = await TestAccount.CreateAsync(factory);

        var list = await ListAsync(account.Client);
        var fromSecond = await ListAsync(second.Client);

        list.Select(d => d.Id).Should().BeEquivalentTo([account.First.DeviceId, second.DeviceId]);
        list.Should().NotContain(d => d.Id == other.First.DeviceId);
        list.Single(d => d.IsCurrent).Id.Should().Be(account.First.DeviceId);
        list.Single(d => d.Id == second.DeviceId).Name.Should().Be("segundo");
        fromSecond.Single(d => d.IsCurrent).Id.Should().Be(second.DeviceId);
    }

    [Fact]
    public async Task Devices_without_token_answers_401()
    {
        await using var factory = new ServerFactory(postgres);
        using var client = factory.CreateClient();

        (await client.GetAsync("/devices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.DeleteAsync($"/devices/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoked_device_gets_401_on_the_next_request()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");

        var revoke = await account.Client.DeleteAsync($"/devices/{second.DeviceId}");

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await second.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Client.GetAsync("/devices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ListAsync(account.Client)).Select(d => d.Id).Should().Equal(account.First.DeviceId);
    }

    [Fact]
    public async Task Revoking_the_current_device_works_like_logout()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        var revoke = await account.Client.DeleteAsync($"/devices/{account.First.DeviceId}");

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoking_an_already_revoked_device_answers_the_same_success()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var second = await account.LoginAsync("segundo");
        await account.Client.DeleteAsync($"/devices/{second.DeviceId}");
        var revokedAt = await RevokedAtAsync(factory, second.DeviceId);
        factory.Time.Advance(TimeSpan.FromMinutes(5));

        var again = await account.Client.DeleteAsync($"/devices/{second.DeviceId}");

        again.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await RevokedAtAsync(factory, second.DeviceId)).Should().Be(revokedAt);
    }

    [Fact]
    public async Task Device_of_another_account_answers_404_and_stays_active()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var other = await TestAccount.CreateAsync(factory);

        var attempt = await account.Client.DeleteAsync($"/devices/{other.First.DeviceId}");
        var missing = await account.Client.DeleteAsync($"/devices/{Guid.NewGuid()}");

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attempt.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("not_found");
        (await missing.Content.ReadAsStringAsync()).Should().Be(await attempt.Content.ReadAsStringAsync());
        (await other.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<DateTime?> RevokedAtAsync(ServerFactory factory, Guid deviceId)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<DateTime?>("SELECT revoked_at FROM devices WHERE id = @deviceId", new { deviceId });
    }
}
