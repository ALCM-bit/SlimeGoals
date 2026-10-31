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

public class ShellViewModelNullSelectionTests
{
    // Revisão final #2: Ctrl+clique no item ativo do ListBox desmarca e o binding TwoWay empurra null.
    [Fact]
    public void Setting_current_page_to_null_keeps_previous_page()
    {
        var shell = new ShellViewModel([new TodayPageViewModel(), new GoalsPageViewModel()]);
        shell.CurrentPage = shell.Pages[1];

        shell.CurrentPage = null!;

        shell.CurrentPage.Should().BeSameAs(shell.Pages[1]);
    }
}
