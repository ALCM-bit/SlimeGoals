using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Create() => new(new PageViewModel[]
        { new TodayPageViewModel(), new GoalsPageViewModel(), new ReportsPageViewModel(), new SettingsPageViewModel() });

    [Fact]
    public void Pages_follow_spec_order_and_start_on_today()
    {
        var shell = Create();
        shell.Pages.Select(p => p.Title).Should().Equal("Hoje", "Metas", "Relatórios", "Configurações");
        shell.CurrentPage.Should().BeOfType<TodayPageViewModel>();
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
