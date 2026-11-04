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
        none.EmptyMessage.Should().Be("Nada para configurar por enquanto.");
    }

    [Fact]
    public void About_shows_version()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()), appVersion: "0.7.0");

        vm.VersionText.Should().Be("SlimeGoals 0.7.0");
        vm.LicenseText.Should().Be("Licença: PolyForm Noncommercial 1.0.0");
    }

    [Fact]
    public void About_default_version_has_no_build_metadata()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()));

        vm.VersionText.Should().StartWith("SlimeGoals ");
        vm.VersionText.Should().NotContain("+");
    }

    [Fact]
    public void Licenses_open_list_then_text()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()));
        (vm.IsMainOpen, vm.IsLicenseListOpen, vm.IsLicenseTextOpen).Should().Be((true, false, false));

        vm.OpenLicensesCommand.Execute(null);
        vm.LicensesOpen.Should().BeTrue();
        (vm.IsMainOpen, vm.IsLicenseListOpen, vm.IsLicenseTextOpen).Should().Be((false, true, false));
        vm.SelectedLicense = vm.Licenses[0];
        vm.SelectedLicenseText.Should().StartWith("Required Notice:");
        (vm.IsMainOpen, vm.IsLicenseListOpen, vm.IsLicenseTextOpen).Should().Be((false, false, true));
        vm.CloseLicenseTextCommand.Execute(null);
        vm.SelectedLicense.Should().BeNull();
        vm.SelectedLicenseText.Should().BeNull();
        vm.LicensesOpen.Should().BeTrue();
        (vm.IsMainOpen, vm.IsLicenseListOpen, vm.IsLicenseTextOpen).Should().Be((false, true, false));
        vm.CloseLicensesCommand.Execute(null);
        vm.LicensesOpen.Should().BeFalse();
        (vm.IsMainOpen, vm.IsLicenseListOpen, vm.IsLicenseTextOpen).Should().Be((true, false, false));
    }

    [Fact]
    public void TryGoBack_closes_text_then_list()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()));
        vm.OpenLicensesCommand.Execute(null);
        vm.SelectedLicense = vm.Licenses[0];

        vm.TryGoBack().Should().BeTrue();
        vm.SelectedLicense.Should().BeNull();
        vm.LicensesOpen.Should().BeTrue();
        vm.TryGoBack().Should().BeTrue();
        vm.LicensesOpen.Should().BeFalse();
        vm.TryGoBack().Should().BeFalse();
    }

    [Fact]
    public async Task Settings_shows_permission_state()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(false, true));
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()), null, null, permissions);
        await vm.LoadCommand.ExecuteAsync(null);

        (vm.ShowPermissions, vm.NotificationsText, vm.ExactAlarmsText, vm.CanRequestNotifications, vm.CanRequestExactAlarms)
            .Should().Be((true, "Bloqueadas", "Permitidos", true, false));
        vm.RequestNotificationsCommand.Execute(null);
        vm.RequestExactAlarmsCommand.Execute(null);
        (permissions.NotificationRequests, permissions.ExactAlarmRequests).Should().Be((1, 1));
    }

    [Fact]
    public async Task Settings_rechecks_permissions_on_data_change()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(false, false));
        var changes = new DataChangeNotifier();
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()), null, null, permissions, changes);
        await vm.LoadCommand.ExecuteAsync(null);

        permissions.Status = new ReminderPermissionStatus(true, true);
        changes.NotifyChanged();

        (vm.NotificationsText, vm.ExactAlarmsText, vm.CanRequestNotifications).Should().Be(("Permitidas", "Permitidos", false));
    }

    [Fact]
    public async Task Settings_shows_battery_state()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(true, true, false));
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()), null, null, permissions);
        await vm.LoadCommand.ExecuteAsync(null);

        (vm.BatteryText, vm.CanRequestBattery).Should().Be(("Otimizada", true));
        vm.RequestBatteryCommand.Execute(null);
        permissions.BatteryRequests.Should().Be(1);
    }

    [Fact]
    public async Task Settings_rechecks_battery_on_data_change()
    {
        var permissions = new FakePermissions(new ReminderPermissionStatus(true, true, false));
        var changes = new DataChangeNotifier();
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()), null, null, permissions, changes);
        await vm.LoadCommand.ExecuteAsync(null);

        permissions.Status = new ReminderPermissionStatus(true, true, true);
        changes.NotifyChanged();

        (vm.BatteryText, vm.CanRequestBattery).Should().Be(("Sem restrição", false));
    }

    [Fact]
    public void Settings_without_port_hides_permissions()
    {
        var vm = new SettingsPageViewModel(new AppSettings(new RecordingSettings()));

        vm.ShowPermissions.Should().BeFalse();
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

    private sealed class FakePermissions(ReminderPermissionStatus status) : IReminderPermissions
    {
        public ReminderPermissionStatus Status { get; set; } = status;
        public int NotificationRequests { get; private set; }
        public int ExactAlarmRequests { get; private set; }
        public int BatteryRequests { get; private set; }

        public ReminderPermissionStatus Get() => Status;

        public void RequestNotifications() => NotificationRequests++;

        public void RequestExactAlarms() => ExactAlarmRequests++;

        public void RequestBatteryUnrestricted() => BatteryRequests++;
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
