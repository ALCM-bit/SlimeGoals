using Avalonia;
using Avalonia.Controls;

namespace SlimeGoals.App.Hosting;

/// <summary>
/// <see cref="IMainWindowPresence"/> da janela principal do desktop. A <see cref="App"/> preenche <see cref="Window"/>
/// ao criá-la, na thread da interface; sem janela, ela não está à vista.
/// </summary>
public sealed class MainWindowPresence : IMainWindowPresence
{
    private Window? _window;
    private volatile bool _onScreen;

    public bool IsOnScreen => _onScreen;

    public event EventHandler? Changed;

    public Window? Window
    {
        get => _window;
        set
        {
            if (_window is not null)
                _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = value;
            if (value is not null)
                value.PropertyChanged += OnWindowPropertyChanged;
            Update();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
            Update();
    }

    private void Update()
    {
        var onScreen = _window is { IsVisible: true, WindowState: not WindowState.Minimized };
        if (onScreen == _onScreen)
            return;
        _onScreen = onScreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
