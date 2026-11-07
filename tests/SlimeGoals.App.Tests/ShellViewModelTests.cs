using Microsoft.Extensions.Time.Testing;
using SlimeGoals.App.Goals;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.Core.Services;
using SlimeGoals.Testing;

namespace SlimeGoals.App.Tests;

public class ShellViewModelTests
{
    internal static SettingsPageViewModel Settings() => new(new AppSettings(new InMemorySettingsRepository()));

    private static GoalsPageViewModel Goals()
    {
        var time = new FakeTimeProvider();
        return new GoalsPageViewModel(new GoalService(new InMemoryGoalRepository(), new InMemoryCheckinRepository(), new InMemoryTimerRepository(), time), time);
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

    private static ShellViewModel Create() => new(new PageViewModel[]
        { Today(), Goals(), Reports(), ShellViewModelTests.Settings() });

    [Fact]
    public void Pages_follow_spec_order_and_start_on_today()
    {
        var shell = Create();
        shell.Pages.Select(p => p.Title).Should().Equal("Hoje", "Metas", "Relatórios", "Configurações");
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
    }

    [Fact]
    public void Pages_have_bottom_nav_icon_keys()
    {
        Create().Pages.Select(p => p.IconKey).Should().Equal("NavToday", "NavGoals", "NavReports", "NavSettings");
    }

    [Fact]
    public void Navigate_changes_current_page_and_notifies()
    {
        var shell = Create();
        using var monitor = shell.Monitor();
        shell.NavigateCommand.Execute(shell.Pages[2]);
        shell.CurrentPage.Should().BeOfType<ReportsPageViewModel>();
        monitor.Should().RaisePropertyChangeFor(s => s.CurrentPage);
    }
}

public class ShellViewModelBackTests
{
    private static ShellViewModel Create(PlatformCapabilities? capabilities = null)
    {
        var time = new FakeTimeProvider();
        var goals = new GoalsPageViewModel(new GoalService(new InMemoryGoalRepository(), new InMemoryCheckinRepository(),
            new InMemoryTimerRepository(), time), time);
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
