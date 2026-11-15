using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.Core.Tests.Services;

public class AppSettingsTests
{
    private readonly InMemorySettingsRepository _settings = new();
    private readonly DataChangeNotifier _changes = new();
    private int _notified;

    public AppSettingsTests() => _changes.Changed += (_, _) => _notified++;

    private AppSettings Sut => new(_settings, _changes);

    [Fact]
    public async Task Snooze_defaults_to_10_and_round_trips()
    {
        (await Sut.GetSnoozeMinutesAsync()).Should().Be(10);

        await Sut.SetSnoozeMinutesAsync(30);

        (await _settings.GetAsync(AppSettings.SnoozeMinutesKey)).Should().Be("30");
        (await Sut.GetSnoozeMinutesAsync()).Should().Be(30);
        _notified.Should().Be(1);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("7")]
    [InlineData("-5")]
    [InlineData("")]
    public async Task Snooze_invalid_stored_value_falls_back_to_10(string stored)
    {
        await _settings.SetAsync(AppSettings.SnoozeMinutesKey, stored);

        (await Sut.GetSnoozeMinutesAsync()).Should().Be(10);
    }

    [Fact]
    public async Task Snooze_rejects_values_outside_options()
    {
        var act = () => Sut.SetSnoozeMinutesAsync(7);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        (await _settings.GetAsync(AppSettings.SnoozeMinutesKey)).Should().BeNull();
        _notified.Should().Be(0);
    }

    [Fact]
    public async Task Close_action_defaults_to_hide_and_round_trips()
    {
        (await Sut.GetCloseActionAsync()).Should().Be(CloseAction.Hide);

        await Sut.SetCloseActionAsync(CloseAction.Quit);

        (await _settings.GetAsync(AppSettings.CloseActionKey)).Should().Be("quit");
        (await Sut.GetCloseActionAsync()).Should().Be(CloseAction.Quit);
        _notified.Should().Be(1);

        await _settings.SetAsync(AppSettings.CloseActionKey, "xyz");
        (await Sut.GetCloseActionAsync()).Should().Be(CloseAction.Hide);
    }

    [Fact]
    public async Task AppSettings_sync_server_defaults_to_local_compose()
    {
        (await Sut.GetSyncServerAsync()).Should().Be(new Uri("http://127.0.0.1:8080"));

        await Sut.SetSyncServerAsync(new Uri("https://sync.exemplo.com"));

        (await _settings.GetAsync(AppSettings.SyncServerKey)).Should().Be("https://sync.exemplo.com/");
        (await Sut.GetSyncServerAsync()).Should().Be(new Uri("https://sync.exemplo.com/"));
        _notified.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("ftp://x")]
    [InlineData("/home/ana")]
    public async Task Sync_server_invalid_stored_value_falls_back_to_local_compose(string stored)
    {
        await _settings.SetAsync(AppSettings.SyncServerKey, stored);

        (await Sut.GetSyncServerAsync()).Should().Be(AppSettings.DefaultSyncServer);
    }

    [Fact]
    public async Task Mini_timer_is_on_by_default_and_remembers_the_choice()
    {
        (await Sut.GetMiniTimerEnabledAsync()).Should().BeTrue();

        await Sut.SetMiniTimerEnabledAsync(false);

        (await _settings.GetAsync(AppSettings.MiniTimerKey)).Should().Be("off");
        (await Sut.GetMiniTimerEnabledAsync()).Should().BeFalse();
        _notified.Should().Be(1);

        await Sut.SetMiniTimerEnabledAsync(true);
        (await Sut.GetMiniTimerEnabledAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task Mini_timer_position_round_trips_without_notifying()
    {
        (await Sut.GetMiniTimerPositionAsync()).Should().BeNull();

        await Sut.SetMiniTimerPositionAsync(12, -40);

        (await _settings.GetAsync(AppSettings.MiniTimerPositionKey)).Should().Be("12,-40");
        (await Sut.GetMiniTimerPositionAsync()).Should().Be((12, -40));
        _notified.Should().Be(0, "a posição não muda dado nenhum que outra tela mostre");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1")]
    [InlineData("1,2,3")]
    [InlineData("")]
    [InlineData("1,")]
    public async Task Mini_timer_position_garbage_reads_as_null(string stored)
    {
        await _settings.SetAsync(AppSettings.MiniTimerPositionKey, stored);

        (await Sut.GetMiniTimerPositionAsync()).Should().BeNull();
    }
}
