using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Sync.Contracts;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class DeviceIdleTests(PostgresFixture postgres)
{
    private static readonly TimeSpan IdleLimit = TimeSpan.FromDays(30);

    private static async Task SetLastSeenAsync(ServerFactory factory, Guid deviceId, DateTimeOffset lastSeen)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.ExecuteAsync(
            "UPDATE devices SET last_seen_at = @lastSeen WHERE id = @deviceId", new { lastSeen, deviceId });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Startup_rejects_device_idle_days_below_one(string days)
    {
        await using var factory = new ServerFactory(postgres, new Dictionary<string, string?> { ["Retention:DeviceIdleDays"] = days });

        var act = () => factory.CreateClient();

        act.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("Retention:DeviceIdleDays");
    }

    [Fact]
    public async Task Device_idle_just_under_30_days_still_authenticates()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await SetLastSeenAsync(factory, account.First.DeviceId, factory.Time.GetUtcNow() - IdleLimit + TimeSpan.FromHours(1));

        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Device_idle_30_days_is_rejected()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        await SetLastSeenAsync(factory, account.First.DeviceId, factory.Time.GetUtcNow() - IdleLimit);

        var response = await account.Client.GetAsync(ServerFactory.ProtectedTestRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync(SyncJsonContext.Default.ErrorResponse))!.Error.Should().Be("unauthorized");
    }

    [Fact]
    public async Task Active_device_never_expires()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);

        for (var i = 0; i < 4; i++)
        {
            factory.Time.Advance(TimeSpan.FromDays(25));
            (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await account.Client.GetAsync(ServerFactory.ProtectedTestRoute)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Maintenance_revokes_idle_devices()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var idle = await account.LoginAsync("parado");
        var now = factory.Time.GetUtcNow();
        await SetLastSeenAsync(factory, idle.DeviceId, now - IdleLimit);
        var store = factory.Services.GetRequiredService<AccountStore>();

        var revoked = await store.RevokeIdleDevicesAsync(now, CancellationToken.None);

        revoked.Should().Be(1);
        var list = (await account.Client.GetFromJsonAsync("/devices", SyncJsonContext.Default.DeviceDtoArray))!;
        list.Select(d => d.Id).Should().Equal(account.First.DeviceId);
        (await store.RevokeIdleDevicesAsync(now, CancellationToken.None)).Should().Be(0);
    }
}
