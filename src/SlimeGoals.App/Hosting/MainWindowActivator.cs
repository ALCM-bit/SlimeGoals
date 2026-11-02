using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// <see cref="IWindowActivator"/> da janela principal do desktop. A <see cref="App"/> preenche
/// <see cref="Window"/> ao criá-la; antes disso, <see cref="Activate"/> não faz nada.
/// </summary>
public sealed class MainWindowActivator : IWindowActivator
{
    private volatile Window? _window;

    public Window? Window
    {
        get => _window;
        set => _window = value;
    }

    public void Activate()
    {
        // Sem janela, o Avalonia pode nem ter iniciado (ex.: segunda instância durante a migração):
        // não toca no Dispatcher antes de saber que a janela existe.
        if (_window is not { } window)
        {
            Trace.TraceInformation("Pedido para mostrar a janela antes de ela existir; ignorado.");
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Show();
            window.Activate();
        });
    }
}
