using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;

namespace SlimeGoals.App;

/// <summary>
/// A App recebe o container pronto do head (Desktop/Android): a composição
/// acontece só nos heads (spec §5); aqui apenas resolvemos o shell.
/// </summary>
public partial class App(IServiceProvider services) : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var shell = services.GetRequiredService<ShellViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = shell };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new ShellView { DataContext = shell };

        base.OnFrameworkInitializationCompleted();
    }
}
