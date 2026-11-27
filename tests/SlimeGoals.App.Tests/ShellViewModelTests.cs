using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Abstractions;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests;

public class ShellViewModelTests
{
    internal static SettingsPageViewModel Settings() => new(new AppSettings(new InMemorySettingsRepository()));

    private static GoalsPageViewModel Goals()
    {
        var time = new FakeTimeProvider();
        var goals = new InMemoryGoalRepository();
        return new GoalsPageViewModel(new GoalService(goals, new InMemoryCheckinRepository(), new InMemoryTimerRepository(), time), time,
            new TagService(new InMemoryTagRepository(), goals, time), new PageNavigator());
    }

    internal static TodayPageViewModel Today()
    {
        var time = new FakeTimeProvider();
        var (goals, checkins, timers) = (new InMemoryGoalRepository(), new InMemoryCheckinRepository(), new InMemoryTimerRepository());
        var checkinService = new CheckinService(goals, checkins, time);
        return new TodayPageViewModel(new ProgressService(goals, checkins, timers, time), checkinService,
            new TimerService(goals, timers, checkinService, time), new GoalService(goals, checkins, timers, time), time,
            new ReminderService(goals, checkins, timers, new InMemoryReminderLogRepository(), new FakeNotificationService(),
                new ReminderFormatter(), time), new DataChangeNotifier());
    }

    private static ReportsPageViewModel Reports()
    {
        var time = new FakeTimeProvider();
        return new ReportsPageViewModel(new ReportService(new InMemoryGoalRepository(), new InMemoryCheckinRepository(),
            new InMemoryReminderLogRepository(), time), time, new DataChangeNotifier());
    }

    internal static TimePageViewModel Time()
    {
        var time = new FakeTimeProvider();
        var (goals, checkins, timers) = (new InMemoryGoalRepository(), new InMemoryCheckinRepository(), new InMemoryTimerRepository());
        var focus = new FocusService(goals, new InMemoryFocusSessionRepository(checkins, timers), timers,
            new CheckinService(goals, checkins, time), new AppSettings(new InMemorySettingsRepository()), time);
        return new TimePageViewModel(new PomodoroViewModel(focus, new GoalService(goals, checkins, timers, time),
            new TagService(new InMemoryTagRepository(), goals, time), time,
            new DataChangeNotifier()));
    }

    private static ShellViewModel Create(PageNavigator? navigator = null) => new(new PageViewModel[]
        { Today(), Goals(), Time(), Reports(), ShellViewModelTests.Settings() }, navigator: navigator);

    [Fact]
    public void Pages_follow_spec_order_and_start_on_today()
    {
        var shell = Create();
        shell.Pages.Select(p => p.Title).Should().Equal("Hoje", "Metas", "Tempo", "Relatórios", "Configurações");
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
    }

    [Fact]
    public void Pages_have_bottom_nav_icon_keys()
    {
        Create().Pages.Select(p => p.IconKey).Should().Equal("NavToday", "NavGoals", "NavTime", "NavReports", "NavSettings");
    }

    [Fact]
    public void Bottom_bar_uses_short_settings_title()
    {
        Create().Pages.Select(p => p.ShortTitle).Should().Equal("Hoje", "Metas", "Tempo", "Relatórios", "Config.");
    }

    [Fact]
    public void Coming_back_to_the_app_resumes_only_the_current_page()
    {
        var (today, time) = (new ResumeCounter("Hoje"), new ResumeCounter("Tempo"));
        var shell = new ShellViewModel(new PageViewModel[] { today, time }) { CurrentPage = time };

        shell.Resume();

        (today.Resumed, time.Resumed).Should().Be((0, 1));
    }

    private sealed class ResumeCounter(string title) : PageViewModel
    {
        public override string Title => title;
        public int Resumed { get; private set; }
        public override void Resume() => Resumed++;
    }

    [Fact]
    public void Navigator_request_switches_the_current_page()
    {
        var navigator = new PageNavigator();
        var shell = Create(navigator);
        using var monitor = shell.Monitor();

        navigator.Show<TimePageViewModel>();
        shell.CurrentPage.Should().BeOfType<TimePageViewModel>();
        monitor.Should().RaisePropertyChangeFor(s => s.CurrentPage);

        navigator.Show<TodayPageViewModel>();
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
    }

    [Fact]
    public void Navigate_changes_current_page_and_notifies()
    {
        var shell = Create();
        using var monitor = shell.Monitor();
        shell.NavigateCommand.Execute(shell.Pages[3]);
        shell.CurrentPage.Should().BeOfType<ReportsPageViewModel>();
        monitor.Should().RaisePropertyChangeFor(s => s.CurrentPage);
    }
}

public class ShellViewModelBackTests
{
    private static ShellViewModel Create(PlatformCapabilities? capabilities = null)
    {
        var time = new FakeTimeProvider();
        var goalRepo = new InMemoryGoalRepository();
        var goals = new GoalsPageViewModel(new GoalService(goalRepo, new InMemoryCheckinRepository(),
            new InMemoryTimerRepository(), time), time, new TagService(new InMemoryTagRepository(), goalRepo, time), new PageNavigator());
        var reports = new ReportsPageViewModel(new ReportService(new InMemoryGoalRepository(), new InMemoryCheckinRepository(),
            new InMemoryReminderLogRepository(), time), time, new DataChangeNotifier());
        return new ShellViewModel([ShellViewModelTests.Today(), goals, reports, ShellViewModelTests.Settings()],
            capabilities: capabilities);
    }

    [Fact]
    public void GoBack_closes_goal_editor_first()
    {
        var shell = Create();
        var goals = (GoalsPageViewModel)shell.Pages[1];
        shell.CurrentPage = goals;
        goals.NewCommand.Execute(null);
        goals.Editor.Should().NotBeNull();

        shell.GoBack().Should().BeTrue();

        goals.Editor.Should().BeNull();
        shell.CurrentPage.Should().BeSameAs(goals);
    }

    [Fact]
    public void GoBack_returns_to_today_then_lets_system_handle()
    {
        var shell = Create();
        shell.CurrentPage = shell.Pages[2];

        shell.GoBack().Should().BeTrue();
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
        shell.GoBack().Should().BeFalse();
    }

    [Fact]
    public void GoBack_walks_out_of_settings_licenses()
    {
        var shell = Create();
        var settings = (SettingsPageViewModel)shell.Pages[3];
        shell.CurrentPage = settings;
        settings.OpenLicensesCommand.Execute(null);
        settings.SelectedLicense = settings.Licenses[0];

        shell.GoBack().Should().BeTrue();
        settings.SelectedLicense.Should().BeNull();
        shell.GoBack().Should().BeTrue();
        settings.LicensesOpen.Should().BeFalse();
        shell.GoBack().Should().BeTrue();
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
        shell.GoBack().Should().BeFalse();
    }

    [Fact]
    public void ShowQuit_follows_capabilities()
    {
        Create().ShowQuit.Should().BeTrue();
        Create(new PlatformCapabilities(false, false, false, false)).ShowQuit.Should().BeFalse();
    }
}

public class ShellViewModelNullSelectionTests
{
    // Revisão final #2: Ctrl+clique no item ativo do ListBox desmarca e o binding TwoWay empurra null.
    [Fact]
    public void Setting_current_page_to_null_keeps_previous_page()
    {
        var shell = new ShellViewModel([ShellViewModelTests.Today(), ShellViewModelTests.Settings()]);
        shell.CurrentPage = shell.Pages[1];

        shell.CurrentPage = null!;

        shell.CurrentPage.Should().BeSameAs(shell.Pages[1]);
    }
}

public class ShellViewModelActivationTests
{
    private sealed class CountingPage(string title) : PageViewModel
    {
        public override string Title => title;
        public int Activations { get; private set; }
        public int Deactivations { get; private set; }
        public override void Activate() => Activations++;
        public override void Deactivate() => Deactivations++;
    }

    private readonly CountingPage _a = new("A");
    private readonly CountingPage _b = new("B");

    [Fact]
    public void Initial_page_is_activated_once()
    {
        _ = new ShellViewModel([_a, _b]);
        _a.Activations.Should().Be(1);
        _b.Activations.Should().Be(0);
    }

    [Fact]
    public void Navigating_activates_target_page()
    {
        var shell = new ShellViewModel([_a, _b]);
        shell.NavigateCommand.Execute(_b);
        shell.NavigateCommand.Execute(_a);

        _b.Activations.Should().Be(1);
        _a.Activations.Should().Be(2);
    }

    [Fact]
    public void Reselecting_current_page_or_null_does_not_activate()
    {
        var shell = new ShellViewModel([_a, _b]);
        shell.CurrentPage = _a;
        shell.CurrentPage = null!;

        _a.Activations.Should().Be(1);
        _a.Deactivations.Should().Be(0);
    }

    [Fact]
    public void Navigating_deactivates_the_page_left()
    {
        var shell = new ShellViewModel([_a, _b]);
        shell.NavigateCommand.Execute(_b);

        (_a.Deactivations, _b.Deactivations).Should().Be((1, 0));
        shell.GoBack();
        (_a.Deactivations, _b.Deactivations).Should().Be((1, 1));
    }
}

public class ShellViewModelQuitTests
{
    private sealed class FakeLifetime : IAppLifetime
    {
        public int Quits { get; private set; }
        public void Quit() => Quits++;
    }

    [Fact]
    public void Quit_command_calls_lifetime()
    {
        var lifetime = new FakeLifetime();
        var shell = new ShellViewModel([ShellViewModelTests.Settings()], lifetime);

        shell.QuitCommand.CanExecute(null).Should().BeTrue();
        shell.QuitCommand.Execute(null);

        lifetime.Quits.Should().Be(1);
    }

    [Fact]
    public void Quit_without_lifetime_cannot_execute()
    {
        var shell = new ShellViewModel([ShellViewModelTests.Settings()]);

        shell.QuitCommand.CanExecute(null).Should().BeFalse();
    }
}

public class ShellViewModelSidebarTests
{
    private readonly InMemorySettingsRepository _store = new();

    private ShellViewModel Create(ISettingsRepository? store = null) =>
        new([ShellViewModelTests.Settings()], settings: new AppSettings(store ?? _store));

    [Fact]
    public async Task Sidebar_starts_open_without_a_saved_choice()
    {
        var shell = Create();
        await shell.SidebarLoad;

        shell.IsSidebarCollapsed.Should().BeFalse();
        shell.SidebarToggleText.Should().Be("Recolher menu");
    }

    [Fact]
    public async Task Sidebar_starts_collapsed_when_it_was_saved_collapsed()
    {
        await _store.SetAsync(AppSettings.SidebarKey, "collapsed");

        var shell = Create();
        await shell.SidebarLoad;

        shell.IsSidebarCollapsed.Should().BeTrue();
        shell.SidebarToggleText.Should().Be("Abrir menu");
    }

    [Fact]
    public async Task Toggle_flips_the_sidebar_and_saves_the_choice()
    {
        var shell = Create();
        await shell.SidebarLoad;
        var changed = new List<string?>();
        shell.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        shell.ToggleSidebarCommand.Execute(null);
        await shell.LastSidebarSave;

        shell.IsSidebarCollapsed.Should().BeTrue();
        (await _store.GetAsync(AppSettings.SidebarKey)).Should().Be("collapsed");
        changed.Should().Contain([nameof(ShellViewModel.IsSidebarCollapsed), nameof(ShellViewModel.SidebarToggleText)]);

        shell.ToggleSidebarCommand.Execute(null);
        await shell.LastSidebarSave;

        shell.IsSidebarCollapsed.Should().BeFalse();
        (await _store.GetAsync(AppSettings.SidebarKey)).Should().Be("open");
    }

    [Fact]
    public async Task A_toggle_before_the_saved_choice_arrives_wins()
    {
        var gated = new GatedSettings();
        await gated.SetAsync(AppSettings.SidebarKey, "collapsed");
        var shell = Create(gated);

        shell.ToggleSidebarCommand.Execute(null);
        gated.Release();
        await shell.SidebarLoad;

        shell.IsSidebarCollapsed.Should().BeTrue("a pessoa recolheu antes de a leitura chegar");
    }

    [Fact]
    public async Task Storage_failures_keep_the_sidebar_working()
    {
        var shell = Create(new ThrowingSettings());
        await shell.SidebarLoad;

        shell.IsSidebarCollapsed.Should().BeFalse();

        shell.ToggleSidebarCommand.Execute(null);
        await shell.LastSidebarSave;

        shell.IsSidebarCollapsed.Should().BeTrue("a falha ao gravar não desfaz a escolha desta sessão");
    }

    [Fact]
    public async Task Without_settings_the_sidebar_still_toggles()
    {
        var shell = new ShellViewModel([ShellViewModelTests.Settings()]);
        await shell.SidebarLoad;

        shell.ToggleSidebarCommand.Execute(null);
        await shell.LastSidebarSave;

        shell.IsSidebarCollapsed.Should().BeTrue();
    }

    /// <summary>Leituras esperam <see cref="Release"/>; gravações vão direto.</summary>
    private sealed class GatedSettings : ISettingsRepository
    {
        private readonly InMemorySettingsRepository _inner = new();
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.SetResult();

        public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        {
            await _gate.Task;
            return await _inner.GetAsync(key, ct);
        }

        public Task SetAsync(string key, string value, CancellationToken ct = default) => _inner.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => _inner.RemoveAsync(key, ct);
    }

    private sealed class ThrowingSettings : ISettingsRepository
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task SetAsync(string key, string value, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }
}
