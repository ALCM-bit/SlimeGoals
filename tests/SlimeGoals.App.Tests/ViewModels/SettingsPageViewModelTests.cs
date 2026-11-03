using AwesomeAssertions;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests.ViewModels;

public class SettingsPageViewModelTests
{
    private static async Task<SettingsPageViewModel> LoadedAsync(RecordingSettings repo, IAutostart? autostart)
    {
        var vm = new SettingsPageViewModel(new AppSettings(repo), autostart);
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public void Sections_follow_capabilities()
    {
        var settings = new AppSettings(new RecordingSettings());
        var all = new SettingsPageViewModel(settings);
        var none = new SettingsPageViewModel(settings, null, new PlatformCapabilities(false, false, false, false));

        (all.ShowGeneral, all.ShowReminders, all.ShowWindow, all.IsEmpty).Should().Be((true, true, true, false));
        (none.ShowGeneral, none.ShowReminders, none.ShowWindow, none.IsEmpty).Should().Be((false, false, false, true));
    }

    [Fact]
    public async Task Load_reads_current_values_without_writing()
    {
        var repo = new RecordingSettings();
        await repo.SetAsync(AppSettings.SnoozeMinutesKey, "15");
        await repo.SetAsync(AppSettings.CloseActionKey, "quit");
        repo.Writes.Clear();

        var vm = await LoadedAsync(repo, new FakeAutostart { Enabled = true });

        vm.StartWithSystem.Should().BeTrue();
        vm.SelectedSnooze!.Minutes.Should().Be(15);
        vm.CloseQuits.Should().BeTrue();
        vm.CloseHint.Should().Be("Com o app fechado, os lembretes param.");
        repo.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_snooze_saves_immediately()
    {
        var repo = new RecordingSettings();
        var vm = await LoadedAsync(repo, null);

        vm.SelectedSnooze = vm.SnoozeOptions.Single(o => o.Label == "30 min");
        await vm.LastSave;

        (await repo.GetAsync(AppSettings.SnoozeMinutesKey)).Should().Be("30");
    }

    [Fact]
    public async Task Changing_close_action_saves_and_updates_hint()
    {
        var repo = new RecordingSettings();
        await repo.SetAsync(AppSettings.CloseActionKey, "quit");
        var vm = await LoadedAsync(repo, null);

        vm.CloseHides = true;
        await vm.LastSave;

        (await repo.GetAsync(AppSettings.CloseActionKey)).Should().Be("hide");
        vm.CloseHint.Should().Be("Os lembretes continuam com a janela fechada.");
    }

    [Fact]
    public async Task Toggling_autostart_calls_enable_and_disable()
    {
        var autostart = new FakeAutostart();
        var vm = await LoadedAsync(new RecordingSettings(), autostart);

        vm.StartWithSystem = true;
        autostart.Enabled.Should().BeTrue();
        vm.StartWithSystem = false;
        autostart.Enabled.Should().BeFalse();
        autostart.Calls.Should().Equal("enable", "disable");
    }

    [Fact]
    public async Task Autostart_failure_reverts_toggle_and_shows_error()
    {
        var autostart = new FakeAutostart { Failure = new IOException("x") };
        var vm = await LoadedAsync(new RecordingSettings(), autostart);

        vm.StartWithSystem = true;

        vm.StartWithSystem.Should().BeFalse();
        vm.ErrorMessage.Should().Be("Não foi possível alterar o início com o sistema.");
    }

    [Fact]
    public async Task Autostart_unexpected_failure_reverts_toggle_and_shows_error()
    {
        var autostart = new FakeAutostart { Failure = new InvalidOperationException("x") };
        var vm = await LoadedAsync(new RecordingSettings(), autostart);

        vm.StartWithSystem = true;

        vm.StartWithSystem.Should().BeFalse();
        vm.ErrorMessage.Should().Be("Não foi possível alterar o início com o sistema.");
    }

    [Fact]
    public async Task Save_failure_reverts_value_and_shows_error()
    {
        var repo = new RecordingSettings { Fail = true };
        var vm = await LoadedAsync(repo, null);
        var before = vm.SelectedSnooze;

        vm.SelectedSnooze = vm.SnoozeOptions.Single(o => o.Minutes == 60);
        await vm.LastSave;

        vm.SelectedSnooze.Should().Be(before);
        vm.ErrorMessage.Should().Be("Não foi possível salvar a configuração.");
    }

    [Fact]
    public async Task Without_autostart_toggle_is_unavailable()
    {
        var vm = await LoadedAsync(new RecordingSettings(), null);

        vm.CanAutostart.Should().BeFalse();
        vm.AutostartUnavailableText.Should().Be("Indisponível neste sistema.");
    }

    [Fact]
    public void Snooze_labels()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new InMemorySettingsRepository()));

        vm.SnoozeOptions.Select(o => o.Label).Should().Equal("5 min", "10 min", "15 min", "30 min", "1 h");
    }

    private sealed class FakeAutostart : IAutostart
    {
        public bool Enabled { get; set; }
        public Exception? Failure { get; init; }
        public List<string> Calls { get; } = [];

        public bool IsEnabled() => Enabled;

        public void Enable()
        {
            Calls.Add("enable");
            if (Failure is not null)
                throw Failure;
            Enabled = true;
        }

        public void Disable()
        {
            Calls.Add("disable");
            if (Failure is not null)
                throw Failure;
            Enabled = false;
        }
    }

    private sealed class RecordingSettings : ISettingsRepository
    {
        private readonly InMemorySettingsRepository _inner = new();
        public List<string> Writes { get; } = [];
        public bool Fail { get; init; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default) => _inner.GetAsync(key, ct);

        public Task SetAsync(string key, string value, CancellationToken ct = default)
        {
            if (Fail)
                throw new InvalidOperationException("boom");
            Writes.Add(key);
            return _inner.SetAsync(key, value, ct);
        }

        public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);
    }
}
