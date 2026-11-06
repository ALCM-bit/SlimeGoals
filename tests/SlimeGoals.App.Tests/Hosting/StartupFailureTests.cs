using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;

namespace SlimeGoals.App.Tests.Hosting;

public class StartupFailureTests
{
    private const string Path = "/home/alguém/.local/share/SlimeGoals/slimegoals.db";

    [Fact]
    public void View_model_exposes_the_message_and_the_database_path()
    {
        var vm = new StartupFailureViewModel(Path);

        vm.Message.Should().Be("Não foi possível abrir os dados do app.");
        vm.DatabasePath.Should().Be(Path);
    }

    [Fact]
    public void Close_command_asks_the_head_to_close()
    {
        var vm = new StartupFailureViewModel(Path);
        var requests = 0;
        vm.CloseRequested += (_, _) => requests++;

        vm.CloseCommand.Execute(null);

        requests.Should().Be(1);
    }

    [Fact]
    public void Close_command_without_listener_does_nothing()
    {
        var act = () => new StartupFailureViewModel(Path).CloseCommand.Execute(null);

        act.Should().NotThrow();
    }

    [Fact]
    public void Container_of_the_failure_carries_only_what_the_error_screen_needs()
    {
        using var services = StartupFailureServices.Create(Path);

        services.GetRequiredService<StartupFailureViewModel>().DatabasePath.Should().Be(Path);
        services.GetRequiredService<MainWindowActivator>().Should().NotBeNull();
        services.GetService<ShellViewModel>().Should().BeNull();
        services.GetServices<IAppHostedService>().Should().BeEmpty();
    }
}
