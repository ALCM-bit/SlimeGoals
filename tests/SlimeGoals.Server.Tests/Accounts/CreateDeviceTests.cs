using System.Security.Cryptography;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SlimeGoals.Server.Accounts;
using SlimeGoals.Sync.Contracts;
using System.Net.Http.Json;

namespace SlimeGoals.Server.Tests.Accounts;

public sealed class CreateDeviceTests(PostgresFixture postgres)
{
    private static async Task<int> DeviceCountAsync(ServerFactory factory, string name)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM devices WHERE name = @name", new { name });
    }

    private static async Task<Guid> AccountIdAsync(ServerFactory factory, string email)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        return await connection.ExecuteScalarAsync<Guid>("SELECT id FROM accounts WHERE email = @email", new { email });
    }

    [Fact]
    public async Task Creating_a_device_with_the_old_hash_after_a_password_change_creates_nothing()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var accountId = await AccountIdAsync(factory, account.Email);
        var oldHash = SecretHash.Of(account.AuthKey);
        var change = PasswordTests.NewPassword(account.AuthKey);
        (await account.Client.PostAsJsonAsync("/auth/password", change, SyncJsonContext.Default.ChangePasswordRequest)).EnsureSuccessStatusCode();
        var store = factory.Services.GetRequiredService<AccountStore>();

        var stale = await store.CreateDeviceAsync(
            accountId, oldHash, "velho", SHA256.HashData(RandomNumberGenerator.GetBytes(8)), DateTimeOffset.UtcNow, CancellationToken.None);
        var fresh = await store.CreateDeviceAsync(
            accountId, SecretHash.Of(change.AuthKey!), "novo", SHA256.HashData(RandomNumberGenerator.GetBytes(8)), DateTimeOffset.UtcNow, CancellationToken.None);

        stale.Should().BeNull();
        fresh.Should().NotBeNull();
        (await DeviceCountAsync(factory, "velho")).Should().Be(0);
        (await DeviceCountAsync(factory, "novo")).Should().Be(1);
    }

    [Fact]
    public async Task Creating_a_device_after_the_account_was_deleted_creates_nothing_and_does_not_throw()
    {
        await using var factory = new ServerFactory(postgres);
        var account = await TestAccount.CreateAsync(factory);
        var accountId = await AccountIdAsync(factory, account.Email);
        var store = factory.Services.GetRequiredService<AccountStore>();
        await store.DeleteAccountAsync(accountId, CancellationToken.None);

        var result = await store.CreateDeviceAsync(
            accountId, SecretHash.Of(account.AuthKey), "fantasma", SHA256.HashData(RandomNumberGenerator.GetBytes(8)), DateTimeOffset.UtcNow, CancellationToken.None);

        result.Should().BeNull();
        (await DeviceCountAsync(factory, "fantasma")).Should().Be(0);
    }
}
