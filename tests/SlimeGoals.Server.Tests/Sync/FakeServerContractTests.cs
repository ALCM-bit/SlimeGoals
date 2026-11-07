using SlimeGoals.Core.Sync;
using SlimeGoals.Testing;

namespace SlimeGoals.Server.Tests.Sync;

public sealed class FakeServerContractTests : SyncContractTests
{
    protected override Task<ISyncTransport> NewAccountAsync() => Task.FromResult<ISyncTransport>(new FakeSyncServer());
}
