using Avalonia.Controls;
using Avalonia.Threading;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// <see cref="IWindowActivator"/> da janela principal do desktop. A <see cref="App"/> preenche
/// <see cref="Window"/> ao criá-la; antes disso, <see cref="Activate"/> não faz nada.
/// </summary>
public sealed class MainWindowActivator : IWindowActivator
{
    public Window? Window { get; set; }

    public void Activate() => Dispatcher.UIThread.Post(() =>
    {
        if (Window is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
    });
}
