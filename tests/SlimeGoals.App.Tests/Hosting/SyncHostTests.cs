using SlimeGoals.App.Hosting;
using SlimeGoals.App.Tests.Account;

namespace SlimeGoals.App.Tests.Hosting;

public class SyncHostTests
{
    [Fact]
    public async Task SyncHost_starts_and_stops_the_account()
    {
        var account = new FakeSyncAccount();
        var host = new SyncHost(account);

        await host.StartAsync();
        account.Calls.Should().Equal("start");

        await host.StopAsync();
        account.Calls.Should().Equal("start", "stop");
    }
}
