using SlimeGoals.App.Hosting;
using SlimeGoals.Platform.Linux.Power;

namespace SlimeGoals.Desktop;

/// <summary>Liga o <see cref="SleepResumeWatcher"/> ao ciclo de vida do app.</summary>
internal sealed class SleepResumeHost(SleepResumeWatcher watcher) : IAppHostedService
{
    public Task StartAsync() => watcher.StartAsync();

    public async Task StopAsync() => await watcher.DisposeAsync().ConfigureAwait(false);
}
