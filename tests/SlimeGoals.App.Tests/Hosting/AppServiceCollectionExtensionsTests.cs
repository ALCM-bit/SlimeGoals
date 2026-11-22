using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Account;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Sync;
using SlimeGoals.Sync;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.Hosting;

/// <summary>Os registros comuns aos dois heads, montados com a mesma validação que eles usam.</summary>
public class AppServiceCollectionExtensionsTests
{
    private static readonly ServiceProviderOptions Validate = new() { ValidateOnBuild = true };

    [Fact]
    public async Task Pages_come_in_navigation_order_with_every_dependency_registered()
    {
        await using var provider = Ports().AddSlimeGoalsApp().BuildServiceProvider(Validate);

        provider.GetServices<PageViewModel>().Select(p => p.GetType()).Should().Equal(
            typeof(TodayPageViewModel), typeof(GoalsPageViewModel), typeof(TimePageViewModel),
            typeof(ReportsPageViewModel), typeof(SettingsPageViewModel));
    }

    [Fact]
    public async Task Sync_passes_the_head_default_server_to_the_account_screen()
    {
        await using var provider = SyncServices(new Uri("http://127.0.0.1:8080")).BuildServiceProvider(Validate);

        provider.GetRequiredService<AccountDefaults>().DefaultServer.Should().Be(new Uri("http://127.0.0.1:8080"));
    }

    [Fact]
    public async Task Sync_without_a_default_server_leaves_the_field_empty()
    {
        await using var provider = SyncServices(null).BuildServiceProvider(Validate);

        provider.GetRequiredService<AccountDefaults>().DefaultServer.Should().BeNull();
    }

    [Fact]
    public async Task Sync_puts_one_coordinator_behind_the_account_screen_and_the_host()
    {
        await using var provider = SyncServices().BuildServiceProvider(Validate);

        var account = provider.GetRequiredService<ISyncAccount>();
        account.Should().BeOfType<SyncCoordinator>().And.BeSameAs(provider.GetRequiredService<SyncCoordinator>());
        provider.GetServices<IAppHostedService>().OfType<SyncHost>().Should().ContainSingle();
        provider.GetRequiredService<AccountDefaults>().DeviceName.Should().Be("pc da Ana");
        var settings = provider.GetServices<PageViewModel>().OfType<SettingsPageViewModel>().Single();
        settings.Account.Should().BeSameAs(provider.GetRequiredService<AccountViewModel>());
    }

    [Fact]
    public async Task A_registered_platform_clipboard_reaches_the_account_screen()
    {
        var clipboard = new UnusedClipboard();
        var services = SyncServices().AddSingleton<ITextClipboard>(clipboard);
        await using var provider = services.BuildServiceProvider(Validate);

        provider.GetRequiredService<AccountViewModel>().PlatformClipboard.Should().BeSameAs(clipboard);
    }

    /// <summary>O que os heads registram por conta própria: repositórios, notificações e o aviso do Pomodoro.</summary>
    private static IServiceCollection Ports()
    {
        var checkins = new InMemoryCheckinRepository();
        var timers = new InMemoryTimerRepository(checkins);
        var log = new InMemoryReminderLogRepository();
        var tags = new InMemoryTagRepository();
        return new ServiceCollection()
            .AddSingleton<ICheckinRepository>(checkins)
            .AddSingleton<ITimerRepository>(timers)
            .AddSingleton<IReminderLogRepository>(log)
            .AddSingleton<ITagRepository>(tags)
            .AddSingleton<IGoalRepository>(new InMemoryGoalRepository(checkins, log, timers, tags))
            .AddSingleton<IFocusSessionRepository>(new InMemoryFocusSessionRepository(checkins, timers))
            .AddSingleton<ISettingsRepository>(new InMemorySettingsRepository())
            .AddSingleton<INotificationService>(new FakeNotificationService())
            .AddSingleton<IFocusAlarm, UnusedFocusAlarm>()
            .AddSingleton<IFocusAlert, UnusedFocusAlert>();
    }

    private static IServiceCollection SyncServices(Uri? defaultServer = null) => Ports()
        .AddSingleton<ISyncStateStore, UnusedSyncStore>()
        .AddSingleton<ISyncLocalStore, UnusedSyncStore>()
        .AddSlimeGoalsApp()
        .AddSlimeGoalsSync(_ => new InMemorySecretVault(), "pc da Ana", defaultServer);

    private sealed class UnusedFocusAlarm : IFocusAlarm
    {
        public void Set(DateTimeOffset at) => throw new NotSupportedException();
        public void Cancel() => throw new NotSupportedException();
    }

    private sealed class UnusedFocusAlert : IFocusAlert
    {
        public void Show(FocusAlertKind kind) => throw new NotSupportedException();
        public void Silence() => throw new NotSupportedException();
        public void Withdraw() => throw new NotSupportedException();
    }

    private sealed class UnusedClipboard : ITextClipboard
    {
        public Task SetTextAsync(string text) => throw new NotSupportedException();
        public Task<string?> TryGetTextAsync() => throw new NotSupportedException();
        public Task ClearAsync() => throw new NotSupportedException();
    }

    /// <summary>O motor nem liga sem conta: nenhum destes métodos pode ser chamado.</summary>
    private sealed class UnusedSyncStore : ISyncStateStore, ISyncLocalStore
    {
        public Task<SyncState> GetAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnableAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task DisableAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OutboxItem>> ReadOutboxAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ReadUnkeyedAsync(int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RememberKeysAsync(IReadOnlyList<OutboxKey> keys, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ConfirmPushAsync(IReadOnlyList<PushConfirmation> confirmations, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ApplyPageAsync(IReadOnlyList<IncomingRecord> records, long lastSeq, Guid deviceId,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task BeginFullResyncAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> FinishFullResyncAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RecordCycleAsync(DateTimeOffset? syncedAt, string? error, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SyncStatus> GetStatusAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
