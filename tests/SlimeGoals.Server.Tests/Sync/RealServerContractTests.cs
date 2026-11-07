using SlimeGoals.Core.Sync;
using SlimeGoals.Server.Tests.Accounts;
using SlimeGoals.Sync;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class RealServerContractTests(PostgresFixture postgres) : SyncContractTests, IAsyncDisposable
{
    private readonly ServerFactory _factory = new(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    protected override async Task<ISyncTransport> NewAccountAsync()
    {
        var account = await TestAccount.CreateAsync(_factory);
        return new HttpSyncTransport(_factory.CreateClient(), _ => ValueTask.FromResult<string?>(account.First.Token));
    }
}
