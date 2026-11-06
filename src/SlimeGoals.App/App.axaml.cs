using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SlimeGoals.App.Hosting;
using SlimeGoals.App.ViewModels;
using SlimeGoals.App.Views;
using SlimeGoals.Core.Services;

namespace SlimeGoals.App;

/// <summary>
/// A App recebe o container pronto do head (Desktop/Android): a composição
/// acontece só nos heads (spec §5); aqui resolvemos o shell, entregamos a janela
/// ao <see cref="MainWindowActivator"/> e ligamos os <see cref="IAppHostedService"/>.
/// </summary>
public partial class App(IServiceProvider services) : Application
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (services.GetService<StartupFailureViewModel>() is { } failure)
        {
            ShowStartupFailure(failure);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var shell = services.GetRequiredService<ShellViewModel>();
        var hosted = services.GetServices<IAppHostedService>().ToList();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Fechar a janela só a oculta: o processo segue para os lembretes até o "Sair" (Shutdown).
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var lifetime = services.GetService<DesktopAppLifetime>();
            var closeBehavior = services.GetService<WindowCloseBehavior>();
            var window = new MainWindow
            {
                DataContext = shell,
                QuitOnClose = () => closeBehavior?.Current == CloseAction.Quit,
                Quit = () => lifetime?.Quit(),
            };
            // Em segundo plano a janela não vai para desktop.MainWindow (que a abriria sozinha):
            // o MainWindowActivator a mostra no primeiro pedido.
            if (services.GetService<AppStartOptions>() is not { Background: true })
                desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, e) =>
            {
                if (!e.Cancel)
                    window.AllowClose = true;
            };
            if (services.GetService<MainWindowActivator>() is { } activator)
                activator.Window = window;
            if (lifetime is not null)
                lifetime.Desktop = desktop;
            desktop.Exit += (_, _) => StopAll(hosted);
            CreateTrayIcon();
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            // A fábrica roda de novo quando a Activity é recriada; o ShellViewModel é singleton
            // (estado preservado) e os serviços sobem uma vez por processo (abaixo).
            activity.MainViewFactory = () => new ShellView { DataContext = shell };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new ShellView { DataContext = shell };

        base.OnFrameworkInitializationCompleted();

        // OnFrameworkInitializationCompleted roda uma vez por processo; a fábrica da view não.
        foreach (var service in hosted)
            _ = StartSafelyAsync(service);
    }

    /// <summary>
    /// O banco não abriu: só a tela de erro, sem shell, bandeja nem serviços. No desktop a janela abre mesmo com
    /// <c>--background</c>, e fechá-la encerra o processo: sem banco, não há lembrete a esperar.
    /// </summary>
    private void ShowStartupFailure(StartupFailureViewModel failure)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            var window = new StartupFailureWindow { DataContext = failure };
            failure.CloseRequested += (_, _) => window.Close();
            desktop.MainWindow = window;
            if (services.GetService<MainWindowActivator>() is { } activator)
                activator.Window = window;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
            activity.MainViewFactory = () => new StartupFailureView { DataContext = failure };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = new StartupFailureView { DataContext = failure };
    }

    /// <summary>
    /// Ícone na bandeja com "Abrir", pausa e "Sair". Opcional: sem suporte (ex.: GNOME sem AppIndicator)
    /// ou com erro, o app segue sem ele.
    /// </summary>
    private void CreateTrayIcon()
    {
        TrayMenuViewModel? tray;
        try
        {
            tray = services.GetService<TrayMenuViewModel>();
            if (tray is null)
                return;

            // PauseText já muda na thread da interface (o VM marshala): basta copiar para o item.
            var pauseItem = new NativeMenuItem { Header = tray.PauseText, Command = tray.TogglePauseCommand };
            tray.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TrayMenuViewModel.PauseText))
                    pauseItem.Header = tray.PauseText;
            };

            var icon = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://SlimeGoals.App/Assets/tray.png"))),
                ToolTipText = "SlimeGoals",
                Menu = new NativeMenu
                {
                    new NativeMenuItem { Header = "Abrir", Command = tray.OpenCommand },
                    pauseItem,
                    new NativeMenuItem { Header = "Sair", Command = tray.QuitCommand },
                },
            };
            icon.Clicked += (_, _) => tray.OpenCommand.Execute(null);
            TrayIcon.SetIcons(this, [icon]);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao criar o ícone da bandeja: {e}");
            return;
        }
        _ = RefreshTraySafelyAsync(tray);
    }

    private static async Task RefreshTraySafelyAsync(TrayMenuViewModel tray)
    {
        try
        {
            await tray.RefreshAsync();
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao atualizar a bandeja: {e}");
        }
    }

    /// <summary>Fora da thread da interface: o início (D-Bus, banco) não pode travar nem derrubar a janela.</summary>
    private static async Task StartSafelyAsync(IAppHostedService service)
    {
        try
        {
            await Task.Run(service.StartAsync).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Falha ao iniciar {service.GetType().Name}: {e}");
        }
    }

    /// <summary>No encerramento, sem depender da thread da interface e com prazo curto por serviço.</summary>
    private static void StopAll(IEnumerable<IAppHostedService> hosted)
    {
        foreach (var service in hosted)
        {
            try
            {
                if (!Task.Run(service.StopAsync).Wait(StopTimeout))
                    Trace.TraceWarning($"{service.GetType().Name} não encerrou em {StopTimeout.TotalSeconds} s.");
            }
            catch (Exception e)
            {
                Trace.TraceError($"Falha ao encerrar {service.GetType().Name}: {e}");
            }
        }
    }
}
