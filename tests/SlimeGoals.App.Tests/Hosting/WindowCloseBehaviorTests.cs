using SlimeGoals.App.Hosting;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Hosting;

public class WindowCloseBehaviorTests
{
    [Fact]
    public async Task WindowCloseBehavior_loads_setting_and_follows_changes()
    {
        var repo = new InMemorySettingsRepository();
        await repo.SetAsync(AppSettings.CloseActionKey, "quit");
        var changes = new DataChangeNotifier();
        var settings = new AppSettings(repo, changes);
        var sut = new WindowCloseBehavior(settings, changes);

        await sut.StartAsync();
        sut.Current.Should().Be(CloseAction.Quit);

        await settings.SetCloseActionAsync(CloseAction.Hide);
        await WaitUntilAsync(() => sut.Current == CloseAction.Hide);
        sut.Current.Should().Be(CloseAction.Hide);

        await sut.StopAsync();
    }

    [Fact]
    public async Task WindowCloseBehavior_defaults_to_hide_when_settings_fail()
    {
        var changes = new DataChangeNotifier();
        var sut = new WindowCloseBehavior(new AppSettings(new ThrowingSettings(), changes), changes);

        var act = () => sut.StartAsync();

        await act.Should().NotThrowAsync();
        sut.Current.Should().Be(CloseAction.Hide);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }

    private sealed class ThrowingSettings : ISettingsRepository
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task SetAsync(string key, string value, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }
}
